using System.Text.RegularExpressions;

namespace ConversorLibros.Core.Services;

/// <summary>A displayable raw sentence and its normalized (spoken) form, whitespace-collapsed.</summary>
public sealed record SentenceSegment(string Raw, string Normalized);

/// <summary>
/// Splits raw chapter text into paragraphs and sentences for the reader. Sentences are split with the chunker's regex
/// and merged back when the split was not a real sentence end (abbreviations such as "Sr." lose their period when
/// normalized, and a lowercase continuation such as "p. m. y" never starts a sentence).
/// </summary>
public static partial class ReaderTextSegmenter
{
    public static IReadOnlyList<string> SplitParagraphs(string rawText) =>
        rawText.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static IReadOnlyList<SentenceSegment> SplitSentences(string rawParagraph, Func<string, string> normalize)
    {
        var pieces = SentenceChunker.SentenceSplitRegex().Split(rawParagraph)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        var sentences = new List<SentenceSegment>();
        string? pending = null;
        for (var i = 0; i < pieces.Count; i++)
        {
            pending = pending is null ? pieces[i] : $"{pending} {pieces[i]}";
            var normalized = Collapse(normalize(pending));
            var isLast = i == pieces.Count - 1;
            if (!isLast && !IsSentenceBoundary(normalized, pieces[i + 1]))
            {
                continue;
            }

            if (isLast && normalized.Length == 0 && sentences.Count > 0)
            {
                // A trailing piece that is not spoken belongs to the previous sentence.
                var previous = $"{sentences[^1].Raw} {pending}";
                sentences[^1] = new SentenceSegment(previous, Collapse(normalize(previous)));
            }
            else
            {
                sentences.Add(new SentenceSegment(pending, normalized));
            }

            pending = null;
        }

        return sentences;
    }

    /// <summary>Collapses whitespace runs to one space and trims, the form used to compare against chunk text.</summary>
    internal static string Collapse(string text) => WhitespaceRegex().Replace(text, " ").Trim();

    private static bool IsSentenceBoundary(string normalizedSoFar, string nextPiece) =>
        normalizedSoFar.Length > 0
        && normalizedSoFar[^1] is '.' or '!' or '?' or '…'
        && !char.IsLower(nextPiece[0]);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
