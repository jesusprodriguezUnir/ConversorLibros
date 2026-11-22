using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using ConversorLibros.Web.Models;

namespace ConversorLibros.Web.Services;

/// <summary>
/// Maps Core book models to API DTOs. Word counts use the same normalizer as ConversionRunner (no pronunciation dictionary)
/// so the numbers describe the audio that will be produced.
/// </summary>
public static class BookDtoMapper
{
    /// <summary>Maximum characters of chapter text returned by the preview endpoint.</summary>
    public const int PreviewMaxLength = 4000;

    private static readonly SpanishTextNormalizer Normalizer = new();

    public static ChapterSummaryDto ToSummary(Chapter chapter)
    {
        var normalized = Normalizer.Normalize(chapter.RawText);
        var words = ChapterTextStatistics.CountWords(normalized);

        return new ChapterSummaryDto(
            Index: chapter.Index,
            Title: chapter.Title,
            CharCount: chapter.RawText.Length,
            WordCount: words,
            EstimatedSeconds: EstimateSeconds(words),
            IsLikelyFrontMatter: ChapterTextStatistics.IsLikelyFrontMatter(chapter.Title, words),
            Excerpt: ChapterTextStatistics.Excerpt(chapter.RawText));
    }

    public static ChapterPreviewDto ToPreview(Chapter chapter)
    {
        var normalized = Normalizer.Normalize(chapter.RawText);
        var words = ChapterTextStatistics.CountWords(normalized);
        var (text, truncated) = TruncateAtWordBoundary(normalized, PreviewMaxLength);

        return new ChapterPreviewDto(
            Index: chapter.Index,
            Title: chapter.Title,
            WordCount: words,
            EstimatedSeconds: EstimateSeconds(words),
            IsLikelyFrontMatter: ChapterTextStatistics.IsLikelyFrontMatter(chapter.Title, words),
            Text: text,
            Truncated: truncated);
    }

    public static BookMetadataDto? ToMetadata(BookMetadata? metadata) =>
        metadata is null
            ? null
            : new BookMetadataDto(
                Publisher: metadata.Publisher,
                PublishedDate: metadata.PublishedDate,
                Description: metadata.Description,
                Isbn: metadata.Isbn,
                Subjects: metadata.Subjects,
                Series: metadata.Series,
                SeriesIndex: metadata.SeriesIndex);

    internal static (string Text, bool Truncated) TruncateAtWordBoundary(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return (text, false);
        }

        var cut = text.LastIndexOfAny([' ', '\n'], maxLength);
        var end = cut > 0 ? cut : maxLength;
        return (text[..end].TrimEnd(), true);
    }

    private static int EstimateSeconds(int words) =>
        (int)Math.Round(ChapterTextStatistics.EstimateDuration(words).TotalSeconds);
}
