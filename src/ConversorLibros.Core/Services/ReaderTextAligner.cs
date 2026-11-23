using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Aligns a chapter's raw text with its chunk timings. Each raw paragraph is segmented and normalized per sentence,
/// then matched against the chunk text stream at chunk boundaries. Matching paragraphs get sentence entries whose
/// times are interpolated by character count inside each chunk; a paragraph that does not match falls back to one
/// entry for the whole paragraph, and alignment resumes at the next paragraph that matches again.
/// </summary>
public static class ReaderTextAligner
{
    public const string SentenceMode = "sentence";
    public const string ParagraphMode = "paragraph";

    /// <summary>How many following paragraphs are tried when looking for the point where alignment resumes.</summary>
    private const int ResyncLookahead = 8;

    public static IReadOnlyList<ReaderParagraph> Align(
        ChapterSyncMap map,
        ITextNormalizer normalizer,
        IReadOnlyDictionary<string, string>? pronunciationDictionary)
    {
        var paragraphs = ReaderTextSegmenter.SplitParagraphs(map.RawText)
            .Select(raw => new RawParagraph(raw, ReaderTextSegmenter.SplitSentences(raw, s => normalizer.Normalize(s, pronunciationDictionary))))
            .ToList();
        if (paragraphs.Count == 0)
        {
            return [];
        }

        var stream = new ChunkStream(map.Chunks);
        var entries = new List<Entry>();
        var cursor = 0;
        var i = 0;
        while (i < paragraphs.Count)
        {
            var paragraph = paragraphs[i];
            if (paragraph.Spoken.Length > 0 && stream.MatchesAt(cursor, paragraph.Spoken))
            {
                var offset = cursor;
                foreach (var sentence in paragraph.Sentences)
                {
                    entries.Add(new Entry(i, sentence.Raw, offset, offset + sentence.Normalized.Length));
                    offset += sentence.Normalized.Length + (sentence.Normalized.Length > 0 ? 1 : 0);
                }

                cursor += paragraph.Spoken.Length + 1;
                i++;
                continue;
            }

            // Mismatch: paragraphs i..resume-1 share the text up to the next paragraph that matches again.
            var (resume, resumeAt) = FindResync(paragraphs, i, cursor, stream);
            var groupEnd = Math.Max(cursor, resumeAt - 1);
            AddFallbackGroup(entries, paragraphs, i, resume, cursor, groupEnd);
            cursor = resumeAt;
            i = resume;
        }

        return BuildParagraphs(entries, stream, map);
    }

    private static (int Paragraph, int Offset) FindResync(List<RawParagraph> paragraphs, int failed, int cursor, ChunkStream stream)
    {
        var best = (Paragraph: paragraphs.Count, Offset: stream.Length + 1);
        var last = Math.Min(paragraphs.Count - 1, failed + ResyncLookahead);
        for (var t = failed + 1; t <= last; t++)
        {
            if (paragraphs[t].Spoken.Length > 0 && stream.FindAnchored(paragraphs[t].Spoken, cursor) is { } at && at < best.Offset)
            {
                best = (t, at);
            }
        }

        return best;
    }

    private static void AddFallbackGroup(List<Entry> entries, List<RawParagraph> paragraphs, int from, int to, int start, int end)
    {
        // Split the shared range by normalized length; every paragraph weighs at least one character.
        var weights = Enumerable.Range(from, to - from).Select(p => Math.Max(1, paragraphs[p].Spoken.Length)).ToList();
        long total = weights.Sum();
        long cumulative = 0;
        var offset = start;
        for (var p = from; p < to; p++)
        {
            cumulative += weights[p - from];
            var next = start + (int)((end - start) * cumulative / total);
            entries.Add(new Entry(p, paragraphs[p].Raw, offset, next, Fallback: true));
            offset = next;
        }
    }

    private static List<ReaderParagraph> BuildParagraphs(List<Entry> entries, ChunkStream stream, ChapterSyncMap map)
    {
        var chunks = map.Chunks;
        var weights = new long[chunks.Count];
        // Only the chunks inside an entry's range can overlap it, so each entry visits just that range.
        foreach (var entry in entries)
        {
            for (var k = stream.FirstChunkEndingAfter(entry.Start); k < chunks.Count && stream.StartOf(k) < entry.End; k++)
            {
                weights[k] += stream.Overlap(k, entry.Start, entry.End);
            }
        }

        var filled = new long[chunks.Count];
        var timed = new List<(Entry Entry, ReaderSentence Sentence)>(entries.Count);
        long previousEnd = 0;
        foreach (var entry in entries)
        {
            var lastChunk = -1;
            for (var k = stream.FirstChunkEndingAfter(entry.Start); k < chunks.Count && stream.StartOf(k) < entry.End; k++)
            {
                var overlap = stream.Overlap(k, entry.Start, entry.End);
                if (overlap > 0)
                {
                    filled[k] += overlap;
                    lastChunk = k;
                }
            }

            var end = lastChunk < 0
                ? previousEnd
                : chunks[lastChunk].StartMs + ((chunks[lastChunk].EndMs - chunks[lastChunk].StartMs) * filled[lastChunk] / weights[lastChunk]);
            end = Math.Max(end, previousEnd);
            timed.Add((entry, new ReaderSentence(entry.Text, previousEnd, end)));
            previousEnd = end;
        }

        // Leftover chunks (text without a raw paragraph) extend the last entry to the end of the chapter.
        var lastEntry = timed[^1];
        timed[^1] = (lastEntry.Entry, lastEntry.Sentence with { EndMs = Math.Max(map.DurationMs, lastEntry.Sentence.EndMs) });

        return timed
            .GroupBy(t => t.Entry.Paragraph)
            .Select(g => new ReaderParagraph(
                g.First().Entry.Fallback ? ParagraphMode : SentenceMode,
                g.Select(t => t.Sentence).ToList()))
            .ToList();
    }

    private sealed record RawParagraph(string Raw, IReadOnlyList<SentenceSegment> Sentences)
    {
        /// <summary>Normalized paragraph text as the chunker would emit it: non-empty sentences joined by one space.</summary>
        public string Spoken { get; } = string.Join(' ', Sentences.Select(s => s.Normalized).Where(n => n.Length > 0));
    }

    /// <summary>A display entry and its range <c>[Start, End)</c> in the chunk stream.</summary>
    private sealed record Entry(int Paragraph, string Text, int Start, int End, bool Fallback = false);

    /// <summary>Chunk texts (whitespace-collapsed) joined by one space, with each chunk's range in that string.</summary>
    private sealed class ChunkStream
    {
        private readonly string _text;
        private readonly int[] _starts;
        private readonly int[] _ends;

        public ChunkStream(IReadOnlyList<ChunkTiming> chunks)
        {
            var texts = chunks.Select(c => ReaderTextSegmenter.Collapse(c.NormalizedText)).ToList();
            _text = string.Join(' ', texts);
            _starts = new int[texts.Count];
            _ends = new int[texts.Count];
            var offset = 0;
            for (var k = 0; k < texts.Count; k++)
            {
                _starts[k] = offset;
                _ends[k] = offset + texts[k].Length;
                offset = _ends[k] + 1;
            }
        }

        public int Length => _text.Length;

        /// <summary>True when <paramref name="spoken"/> starts at a chunk start and ends exactly at a chunk end.</summary>
        /// <remarks>Chunk starts and ends are strictly ascending, so lookups use binary search.</remarks>
        public bool MatchesAt(int offset, string spoken) =>
            Array.BinarySearch(_starts, offset) >= 0
            && Array.BinarySearch(_ends, offset + spoken.Length) >= 0
            && string.CompareOrdinal(_text, offset, spoken, 0, spoken.Length) == 0;

        public int? FindAnchored(string spoken, int from)
        {
            var first = Array.BinarySearch(_starts, from);
            for (var k = first >= 0 ? first : ~first; k < _starts.Length; k++)
            {
                if (MatchesAt(_starts[k], spoken))
                {
                    return _starts[k];
                }
            }

            return null;
        }

        /// <summary>Index of the first chunk whose range ends after <paramref name="offset"/>; chunks before it cannot overlap a range starting there.</summary>
        public int FirstChunkEndingAfter(int offset)
        {
            var k = Array.BinarySearch(_ends, offset);
            return k >= 0 ? k + 1 : ~k;
        }

        public int StartOf(int chunk) => _starts[chunk];

        public long Overlap(int chunk, int start, int end) =>
            Math.Max(0, Math.Min(end, _ends[chunk]) - Math.Max(start, _starts[chunk]));
    }
}
