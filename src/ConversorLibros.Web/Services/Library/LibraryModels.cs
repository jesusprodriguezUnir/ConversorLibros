namespace ConversorLibros.Web.Services.Library;

public sealed record BookRecord
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string SortTitle { get; init; } = string.Empty;
    public string? Author { get; init; }
    public string? Series { get; init; }
    public double? SeriesIndex { get; init; }
    public string? Publisher { get; init; }
    public string? PublishedDate { get; init; }
    public string? Isbn { get; init; }
    public string? Description { get; init; }
    public string? Language { get; init; }
    public string? EpubPath { get; init; }
    public DateTimeOffset? EpubLastWriteUtc { get; init; }
    public string? SourceSha256 { get; init; }
    public string? FolderPath { get; init; }
    public bool IsLegacy { get; init; }
    public bool HasEpub { get; init; }
    public bool HasCover { get; init; }
    public string AudioStatus { get; init; } = "none";
    public string? AudioFormat { get; init; }
    public long? AudioDurationMs { get; init; }
    public int ChapterCount { get; init; }
    public string? VoiceUsed { get; init; }
    public int? ReadingChapter { get; init; }
    public double ReadingPercent { get; init; }
    public DateTimeOffset? LastReadAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record LibraryQuery(
    string? Query = null,
    string? Author = null,
    string? Series = null,
    string? Tag = null,
    string? AudioStatus = null,
    string? Sort = "title",
    string? Dir = "asc");

public sealed record FacetItemDto(string Name, int Count);

public sealed record LibraryCountsDto(int All, int WithAudio, int TextOnly, int Converting);

public sealed record LibraryFacetsDto(
    LibraryCountsDto Counts,
    IReadOnlyList<FacetItemDto> Authors,
    IReadOnlyList<FacetItemDto> Series,
    IReadOnlyList<FacetItemDto> Tags);

public sealed record LibrarySearchResult(
    IReadOnlyList<BookRecord> Items,
    int TotalCount,
    LibraryFacetsDto Facets);

public sealed record UpdateBookMetadataRequest(
    string? Title = null,
    string? Author = null,
    string? Series = null,
    double? SeriesIndex = null,
    string? Publisher = null,
    string? PublishedDate = null,
    string? Isbn = null,
    string? Description = null,
    string? Language = null,
    IReadOnlyList<string>? Tags = null);
