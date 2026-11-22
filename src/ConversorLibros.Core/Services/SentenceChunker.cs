using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Splits normalized chapter text into sentence/paragraph chunks under a character limit,
/// computing a deterministic SHA-256 hash for caching.
/// </summary>
public sealed partial class SentenceChunker : IChunker
{
    private readonly int _maxChunkChars;

    public SentenceChunker(int maxChunkChars = 350)
    {
        if (maxChunkChars < 50)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChunkChars), "Must be at least 50 characters.");
        }

        _maxChunkChars = maxChunkChars;
    }

    public IReadOnlyList<TextChunk> Chunk(Chapter chapter, string normalizedText, string voiceId, float speed, string modelVersion)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return [];
        }

        var paragraphs = normalizedText
            .Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var chunks = new List<TextChunk>();
        var order = 0;

        for (var p = 0; p < paragraphs.Length; p++)
        {
            var isLastParagraph = p == paragraphs.Length - 1;
            var sentences = SentenceSplitRegex().Split(paragraphs[p])
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim());

            var buffer = new StringBuilder();
            foreach (var sentence in sentences)
            {
                if (buffer.Length > 0 && buffer.Length + 1 + sentence.Length > _maxChunkChars)
                {
                    chunks.Add(CreateChunk(chapter.Index, order++, buffer.ToString(), TimeSpan.FromMilliseconds(150), voiceId, speed, modelVersion));
                    buffer.Clear();
                }

                if (sentence.Length > _maxChunkChars)
                {
                    if (buffer.Length > 0)
                    {
                        chunks.Add(CreateChunk(chapter.Index, order++, buffer.ToString(), TimeSpan.FromMilliseconds(150), voiceId, speed, modelVersion));
                        buffer.Clear();
                    }

                    foreach (var part in SplitLongSentence(sentence, _maxChunkChars))
                    {
                        chunks.Add(CreateChunk(chapter.Index, order++, part, TimeSpan.FromMilliseconds(150), voiceId, speed, modelVersion));
                    }
                    continue;
                }

                if (buffer.Length > 0)
                {
                    buffer.Append(' ');
                }
                buffer.Append(sentence);
            }

            if (buffer.Length > 0)
            {
                var pause = isLastParagraph ? TimeSpan.FromMilliseconds(1500) : TimeSpan.FromMilliseconds(300);
                chunks.Add(CreateChunk(chapter.Index, order++, buffer.ToString(), pause, voiceId, speed, modelVersion));
            }
        }

        return chunks;
    }

    private static IEnumerable<string> SplitLongSentence(string sentence, int maxChars)
    {
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }
            current.Append(word);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static TextChunk CreateChunk(
        int chapterIndex,
        int order,
        string text,
        TimeSpan pauseAfter,
        string voiceId,
        float speed,
        string modelVersion)
    {
        var key = $"{text}|{voiceId}|{speed:F2}|{modelVersion}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var hash = Convert.ToHexStringLower(hashBytes);
        return new TextChunk(chapterIndex, order, text, pauseAfter, hash);
    }

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceSplitRegex();
}
