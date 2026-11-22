using System.Globalization;
using System.Text;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Word counts, listening-time estimates and front-matter heuristics for chapter text.
/// Pass normalized text (see <c>SpanishTextNormalizer</c>) so counts track what the TTS actually speaks.
/// </summary>
public static class ChapterTextStatistics
{
    /// <summary>Narration pace used for the listening-time estimate.</summary>
    public const int WordsPerMinute = 150;

    /// <summary>Chapters shorter than this many words are flagged as likely front or back matter.</summary>
    public const int FrontMatterWordThreshold = 150;

    /// <summary>Default maximum length of an excerpt, in characters.</summary>
    public const int DefaultExcerptLength = 240;

    // Compared against the whole title after removing diacritics and lowercasing. Never substring-matched.
    private static readonly HashSet<string> FrontMatterTitles = new(StringComparer.Ordinal)
    {
        "copyright", "indice", "contents", "table of contents", "dedicatoria", "dedication",
        "portadilla", "creditos", "portada", "cover", "colofon"
    };

    /// <summary>
    /// Counts tokens that contain at least one letter or digit, so dialogue dashes and lone punctuation do not count.
    /// </summary>
    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var count = 0;
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Any(char.IsLetterOrDigit))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Estimated narration time for the given number of words at <see cref="WordsPerMinute"/>.</summary>
    public static TimeSpan EstimateDuration(int words) =>
        TimeSpan.FromMinutes(words / (double)WordsPerMinute);

    /// <summary>
    /// Returns a single-line excerpt no longer than <paramref name="maxLength"/> characters, cut at a word boundary
    /// and suffixed with an ellipsis when truncated.
    /// </summary>
    public static string Excerpt(string? text, int maxLength = DefaultExcerptLength)
    {
        if (maxLength <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Excerpt length must be greater than 1.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= maxLength)
        {
            return collapsed;
        }

        var limit = maxLength - 1; // room for the ellipsis
        var cut = collapsed.LastIndexOf(' ', limit);
        var end = cut > 0 ? cut : limit;
        return collapsed[..end].TrimEnd(',', ';', ':', '.', '-', '—') + "…";
    }

    /// <summary>
    /// Heuristic for chapters that are probably not narrative content: very short chapters, or titles that match
    /// a known front/back-matter heading exactly (accent- and case-insensitive).
    /// </summary>
    public static bool IsLikelyFrontMatter(string title, int words)
    {
        if (words < FrontMatterWordThreshold)
        {
            return true;
        }

        var key = RemoveDiacritics(title ?? string.Empty).Trim().ToLowerInvariant();
        return FrontMatterTitles.Contains(key);
    }

    internal static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
