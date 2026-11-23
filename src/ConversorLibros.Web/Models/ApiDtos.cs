namespace ConversorLibros.Web.Models;

public sealed record WebSettings(
    string PiperModelDir,
    string OutputRoot,
    string OllamaUrl,
    string OllamaModel,
    string FfmpegOverride,
    float DefaultSpeed = 1.0f,
    string DefaultFormat = "opus");

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
    string? CoverUrl);

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
