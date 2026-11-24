namespace ConversorLibros.Web.Models;

public sealed record WebSettings(
    string PiperModelDir,
    string OutputRoot,
    string OllamaUrl,
    string OllamaModel,
    string FfmpegOverride,
    float DefaultSpeed = 1.0f,
    string DefaultFormat = "opus",
    string BooksRoot = "");

public sealed record PiperValidationStatus(bool IsValid, string Message);

public sealed record FfmpegStatus(bool Found, string? ResolvedPath, string Message);

/// <param name="EffectivePiperModelDir">Folder actually used for synthesis: the configured one, or the auto-detected one.</param>
/// <param name="PiperAutoDetected">True when the configured folder is invalid and a model was found elsewhere.</param>
public sealed record SettingsResponse(
    WebSettings Settings,
    PiperValidationStatus Piper,
    FfmpegStatus Ffmpeg,
    string EffectivePiperModelDir,
    bool PiperAutoDetected);

public sealed record TestOllamaRequest(string Url, string Model);

public sealed record TestOllamaResponse(
    bool Connected,
    bool ModelInstalled,
    IReadOnlyList<string> Models,
    string Message);

public sealed record DirectoryEntryDto(string Name, string Path);

public sealed record FileSystemBrowseResponse(
    string? CurrentPath,
    string? ParentPath,
    IReadOnlyList<DirectoryEntryDto> Directories);

public sealed record ErrorResponse(string Error);

public sealed record LibraryItemDto(
    string Id,
    string Title,
    string Author,
    string Format,
    int ChapterCount,
    double? DurationSeconds,
    bool HasCover,
    string? CoverUrl,
    bool HasReader = false);

public sealed record ReaderIndexChapter(int Index, string Title, long DurationMs, string? AudioKind);

/// <summary>On-disk <c>reader/index.json</c> (camelCase, schemaVersion 1).</summary>
public sealed record ReaderIndexFile(int SchemaVersion, string Title, IReadOnlyList<ReaderIndexChapter>? Chapters);

/// <param name="Available">False when the book has no usable reader data; <c>Reason</c> then says why.</param>
public sealed record ReaderIndexResponse(
    bool Available,
    string? Reason,
    string? Title,
    bool HasCover,
    IReadOnlyList<ReaderIndexChapter> Chapters);

public sealed record ReaderProgress(int ChapterIndex, long Ms, DateTimeOffset UpdatedAt);

public sealed record ReaderBookmark(
    Guid Id, int ChapterIndex, long Ms, string? Excerpt, string? Note, DateTimeOffset CreatedAt);

public sealed record SaveProgressRequest(int ChapterIndex, long Ms);

public sealed record CreateBookmarkRequest(int ChapterIndex, long Ms, string? Note, string? Excerpt);

public sealed record UpdateBookmarkNoteRequest(string? Note);

/// <summary>
/// Chapter summary for the inspect response. Word and duration figures are computed on normalized text
/// (numbers spelled out) so they track what the TTS speaks; <c>Excerpt</c> comes from the raw text.
/// </summary>
public sealed record ChapterSummaryDto(
    int Index,
    string Title,
    int CharCount,
    int WordCount,
    int EstimatedSeconds,
    bool IsLikelyFrontMatter,
    string Excerpt);

public sealed record BookMetadataDto(
    string? Publisher,
    string? PublishedDate,
    string? Description,
    string? Isbn,
    IReadOnlyList<string> Subjects,
    string? Series,
    double? SeriesIndex);

public sealed record InspectedBookDto(
    string UploadId,
    string Title,
    string Author,
    string Language,
    bool HasCover,
    string? CoverUrl,
    IReadOnlyList<ChapterSummaryDto> Chapters,
    string FolderName,
    BookMetadataDto? Metadata,
    int TotalWordCount,
    int TotalEstimatedSeconds);

/// <summary>
/// Chapter text as the TTS will read it (normalized, not translated), truncated for transport.
/// </summary>
public sealed record ChapterPreviewDto(
    int Index,
    string Title,
    int WordCount,
    int EstimatedSeconds,
    bool IsLikelyFrontMatter,
    string Text,
    bool Truncated);

public sealed record StartConversionRequest(
    string UploadId,
    IReadOnlyList<int> ChapterIndexes,
    float Speed,
    bool Translate,
    string? SourceLanguage,
    string Format);

public sealed record ConversionStateDto(
    string Status,
    string Stage,
    double ProgressFraction,
    string CurrentChapter,
    string Timing,
    string? BookTitle,
    string? OutputPath,
    string? Warning,
    string? Error,
    IReadOnlyList<string> Logs);
