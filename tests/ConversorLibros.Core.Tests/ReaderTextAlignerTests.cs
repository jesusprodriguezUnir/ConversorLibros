using System.Diagnostics;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public sealed class ReaderTextAlignerTests
{
    private const long MsPerChar = 10;
    private static readonly SpanishTextNormalizer Normalizer = new();

    [Fact]
    public void Align_SentencesInsideOneChunk_InterpolatesByNormalizedLength()
    {
        var first = new string('a', 99) + ".";
        var second = "B" + new string('b', 298) + ".";
        var map = new ChapterSyncMap(0, "c", $"{first} {second}", 1000, [new ChunkTiming(0, $"{first} {second}", 0, 1000)]);

        var paragraph = Assert.Single(ReaderTextAligner.Align(map, Normalizer, null));

        Assert.Equal("sentence", paragraph.Mode);
        Assert.Equal([(0L, 250L), (250L, 1000L)], paragraph.Sentences.Select(s => (s.StartMs, s.EndMs)));
    }

    [Fact]
    public void Align_SentenceSpanningChunks_StartsAndEndsAtItsChunkPortions()
    {
        const string raw = "Corta. Esta frase es tan larga que el troceador la parte en varios trozos por palabras sin remedio. Fin.";
        var map = BuildMap(raw);
        Assert.True(map.Chunks.Count >= 4);

        var paragraph = Assert.Single(ReaderTextAligner.Align(map, Normalizer, null));

        Assert.Equal("sentence", paragraph.Mode);
        Assert.Equal(3, paragraph.Sentences.Count);
        var spanning = paragraph.Sentences[1];
        Assert.Equal(map.Chunks[1].StartMs, spanning.StartMs);
        Assert.Equal(map.Chunks[^2].EndMs, spanning.EndMs);
        AssertCoverage([paragraph], map.DurationMs);
    }

    [Fact]
    public void Align_MismatchInOneParagraph_FallsBackOnlyForThatParagraph()
    {
        const string spoken = "Uno. Dos.\n\nTres. Cuatro.\n\nCinco seis.\n\nSiete. Ocho.";
        var map = BuildMap(spoken) with { RawText = spoken.Replace("Cinco seis.", "Texto distinto y bastante más largo.") };

        var paragraphs = ReaderTextAligner.Align(map, Normalizer, null);

        Assert.Equal(["sentence", "sentence", "paragraph", "sentence"], paragraphs.Select(p => p.Mode));
        Assert.Equal("Texto distinto y bastante más largo.", Assert.Single(paragraphs[2].Sentences).Text);
        Assert.Equal(map.Chunks[^1].StartMs, paragraphs[3].Sentences[0].StartMs);
        AssertCoverage(paragraphs, map.DurationMs);
    }

    [Fact]
    public void Align_RomanNumeralAcrossParagraphBreak_FallsBackWithoutAffectingNextParagraph()
    {
        var map = BuildMap("Capítulo\n\nIV\n\nEra de noche. Llovía.");

        var paragraphs = ReaderTextAligner.Align(map, Normalizer, null);

        Assert.Equal(["paragraph", "paragraph", "sentence"], paragraphs.Select(p => p.Mode));
        Assert.Equal(["Era de noche.", "Llovía."], paragraphs[2].Sentences.Select(s => s.Text));
        AssertCoverage(paragraphs, map.DurationMs);
    }

    [Fact]
    public void Align_LeftoverChunks_ExtendTheLastParagraph()
    {
        var map = BuildMap("Uno. Dos.\n\nTres.") with { RawText = "Uno. Dos." };

        var paragraph = Assert.Single(ReaderTextAligner.Align(map, Normalizer, null));

        Assert.Equal(map.DurationMs, paragraph.Sentences[^1].EndMs);
    }

    [Fact]
    public void Align_DisplaysOriginalTextAndHonoursPronunciationDictionary()
    {
        var dictionary = new Dictionary<string, string> { ["Nguyen"] = "Nuyen" };
        var map = BuildMap("Subió un 25 % en 2020. Lo dijo Nguyen.", dictionary);

        var paragraph = Assert.Single(ReaderTextAligner.Align(map, Normalizer, dictionary));

        Assert.Equal("sentence", paragraph.Mode);
        Assert.Equal(["Subió un 25 % en 2020.", "Lo dijo Nguyen."], paragraph.Sentences.Select(s => s.Text));
    }

    [Fact]
    public void Align_BlankParagraphs_ProduceNoEntriesAndDoNotShiftTimings()
    {
        var clean = BuildMap("Uno.\n\nDos.");
        var withBlanks = clean with { RawText = "\n\n  \n\nUno.\n\n \t \n\n\n\nDos.\n\n" };

        var expected = ReaderTextAligner.Align(clean, Normalizer, null);
        var actual = ReaderTextAligner.Align(withBlanks, Normalizer, null);

        Assert.Equal(2, actual.Count);
        Assert.Equal(Flatten(expected), Flatten(actual));
    }

    [Fact]
    public void Align_BlankRawText_ReturnsNoParagraphs()
    {
        var map = BuildMap("Uno. Dos.") with { RawText = "  \n\n \t " };

        Assert.Empty(ReaderTextAligner.Align(map, Normalizer, null));
    }

    [Fact]
    public void Align_NoChunks_FallsBackToParagraphsWithZeroTimes()
    {
        var map = new ChapterSyncMap(0, "c", "Uno.\n\nDos.", 0, []);

        var paragraphs = ReaderTextAligner.Align(map, Normalizer, null);

        Assert.Equal(["paragraph", "paragraph"], paragraphs.Select(p => p.Mode));
        Assert.All(paragraphs.SelectMany(p => p.Sentences), s => Assert.Equal((0L, 0L), (s.StartMs, s.EndMs)));
    }

    [Fact]
    public void Align_ResyncNeverFound_RestOfChapterFallsBackAndStillCoversAudio()
    {
        var map = BuildMap("Uno. Dos.\n\nTres. Cuatro.\n\nCinco.") with { RawText = "Uno. Dos.\n\nAlgo distinto.\n\nOtra cosa." };

        var paragraphs = ReaderTextAligner.Align(map, Normalizer, null);

        Assert.Equal(["sentence", "paragraph", "paragraph"], paragraphs.Select(p => p.Mode));
        AssertCoverage(paragraphs, map.DurationMs);
    }

    [Fact]
    public void Align_AlignedParagraphs_StartAndEndOnChunkBoundaries()
    {
        const string raw = "Primera frase corta. Segunda frase algo más larga que la primera.\n\nOtro párrafo. Con dos frases.\n\nÚltimo.";
        var map = BuildMap(raw);
        var starts = map.Chunks.Select(c => c.StartMs).ToHashSet();
        var ends = map.Chunks.Select(c => c.EndMs).ToHashSet();

        var paragraphs = ReaderTextAligner.Align(map, Normalizer, null);

        Assert.Equal(3, paragraphs.Count);
        Assert.All(paragraphs, p =>
        {
            Assert.Equal("sentence", p.Mode);
            Assert.Contains(p.Sentences[0].StartMs, starts);
            Assert.Contains(p.Sentences[^1].EndMs, ends);
        });
        AssertCoverage(paragraphs, map.DurationMs);
    }

    [Fact]
    public void Align_LargeChapterWithManyMismatches_CompletesQuickly()
    {
        var spoken = Enumerable.Range(1, 10_000).Select(i => $"Frase {i} del libro.").ToList();
        var raw = spoken.Select((p, i) => i % 20 is 5 or 6 ? $"Texto ausente {i}." : p);
        var map = BuildMap(string.Join("\n\n", spoken)) with { RawText = string.Join("\n\n", raw) };

        var watch = Stopwatch.StartNew();
        var paragraphs = ReaderTextAligner.Align(map, Normalizer, null);
        watch.Stop();

        Assert.Equal(10_000, paragraphs.Count);
        Assert.Equal(1_000, paragraphs.Count(p => p.Mode == "paragraph"));
        AssertCoverage(paragraphs, map.DurationMs);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Alignment took {watch.Elapsed}.");
    }

    private static ChapterSyncMap BuildMap(string raw, IReadOnlyDictionary<string, string>? dictionary = null)
    {
        var chapter = new Chapter(0, "c", raw);
        var chunks = new SentenceChunker(maxChunkChars: 50).Chunk(chapter, Normalizer.Normalize(raw, dictionary), "0", 1f, "m");
        var timings = new List<ChunkTiming>();
        long cursor = 0;
        foreach (var chunk in chunks)
        {
            var end = cursor + (chunk.NormalizedText.Length * MsPerChar);
            timings.Add(new ChunkTiming(chunk.Order, chunk.NormalizedText, cursor, end));
            cursor = end;
        }

        return new ChapterSyncMap(0, "c", raw, cursor, timings);
    }

    private static IEnumerable<(string, long, long)> Flatten(IEnumerable<ReaderParagraph> paragraphs) =>
        paragraphs.SelectMany(p => p.Sentences).Select(s => (s.Text, s.StartMs, s.EndMs)).ToList();

    private static void AssertCoverage(IReadOnlyList<ReaderParagraph> paragraphs, long durationMs)
    {
        var entries = paragraphs.SelectMany(p => p.Sentences).ToList();
        Assert.Equal(0, entries[0].StartMs);
        for (var i = 0; i < entries.Count; i++)
        {
            Assert.True(entries[i].EndMs >= entries[i].StartMs, $"Entry {i} is inverted.");
            if (i > 0)
            {
                Assert.Equal(entries[i - 1].EndMs, entries[i].StartMs);
            }
        }

        Assert.Equal(durationMs, entries[^1].EndMs);
    }
}
