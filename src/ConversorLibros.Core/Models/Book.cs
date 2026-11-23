namespace ConversorLibros.Core.Models;

/// <summary>
/// Represents a parsed book ready for normalization and TTS synthesis.
/// </summary>
public sealed record Book(
    string Title,
    string Author,
    string Language,
    IReadOnlyList<Chapter> Chapters,
    byte[]? CoverImage = null,
    string? CoverMimeType = null,
    BookMetadata? Metadata = null);

/// <summary>
/// Optional catalogue metadata read from the EPUB package. Every field is best-effort and may be absent.
/// </summary>
/// <param name="Publisher">First publisher listed in the package.</param>
/// <param name="PublishedDate">Publication date as written in the package (may be only a year), or null.</param>
/// <param name="Description">Description as plain text (HTML stripped), paragraphs separated by blank lines.</param>
/// <param name="Isbn">ISBN digits only (no hyphens), or null when the package has no ISBN-like identifier.</param>
/// <param name="Subjects">Subject tags, de-duplicated, in package order.</param>
/// <param name="Series">Series name from EPUB 3 belongs-to-collection or Calibre series metadata.</param>
/// <param name="SeriesIndex">Position inside the series (for example 2 for "#2").</param>
public sealed record BookMetadata(
    string? Publisher,
    string? PublishedDate,
    string? Description,
    string? Isbn,
    IReadOnlyList<string> Subjects,
    string? Series,
    double? SeriesIndex);

/// <summary>
/// Represents a single chapter extracted from the book.
/// </summary>
public sealed record Chapter(
    int Index,
    string Title,
    string RawText);

/// <summary>
/// Represents a normalized, TTS-ready text segment with a deterministic content hash.
/// </summary>
public sealed record TextChunk(
    int ChapterIndex,
    int Order,
    string NormalizedText,
    TimeSpan PauseAfter,
    string ContentHash);

/// <summary>
/// Reports progress during book conversion.
/// </summary>
public sealed record ConversionProgress(
    int ChapterIndex,
    int TotalChapters,
    int CompletedChunks,
    int TotalChunks,
    string Stage);
