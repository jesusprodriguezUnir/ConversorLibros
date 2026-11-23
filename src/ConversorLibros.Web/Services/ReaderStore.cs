using System.Collections.Concurrent;
using System.Text.Json;
using ConversorLibros.Web.Models;

namespace ConversorLibros.Web.Services;

public enum ReaderStoreStatus { Ok, NotFound, Invalid, UnsupportedVersion }

/// <summary>
/// Reads the read-along sidecars of a book and owns its mutable state (<c>progress.json</c>, <c>bookmarks.json</c>).
/// Every read and write of those files goes through one <see cref="SemaphoreSlim"/> per book; writes go to
/// <c>&lt;file&gt;.partial</c> and are then moved into place. Malformed files count as empty and are replaced by the
/// next write; files with an unknown <c>schemaVersion</c> count as empty but are never overwritten.
/// </summary>
public sealed class ReaderStore(ILogger<ReaderStore> logger)
{
    public const int MaxNoteLength = 500;
    public const int MaxExcerptLength = 160;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    private enum LoadState { Missing, Ok, Corrupt, Unsupported }

    /// <summary>The book's index, or null when it is absent, malformed or from an unknown schema.</summary>
    public async Task<ReaderIndexFile?> ReadIndexAsync(string bookDir, CancellationToken ct)
    {
        var (state, index) = await LoadAsync<ReaderIndexFile>(IndexPath(bookDir), f => f.SchemaVersion, ct).ConfigureAwait(false);
        return state == LoadState.Ok ? index : null;
    }

    /// <summary>Raw chapter JSON. Null when absent; <see cref="InvalidDataException"/> when malformed.</summary>
    public async Task<byte[]?> ReadChapterBytesAsync(string bookDir, int chapterIndex, CancellationToken ct)
    {
        var path = Path.Combine(ReaderDir(bookDir), $"chapter_{chapterIndex:D3}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        try
        {
            using var _ = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Malformed chapter file '{Path.GetFileName(path)}'.", ex);
        }

        return bytes;
    }

    public async Task<ReaderProgress?> GetProgressAsync(string bookDir, CancellationToken ct)
    {
        using var guard = await EnterAsync(bookDir, ct).ConfigureAwait(false);
        var (state, file) = await LoadAsync<ProgressFile>(ProgressPath(bookDir), f => f.SchemaVersion, ct).ConfigureAwait(false);
        return state == LoadState.Ok ? file!.ToProgress() : null;
    }

    public async Task<(ReaderStoreStatus Status, ReaderProgress? Progress)> PutProgressAsync(
        string bookDir, int chapterIndex, long ms, CancellationToken ct)
    {
        if (ms < 0 || !await HasChapterAsync(bookDir, chapterIndex, ct).ConfigureAwait(false))
        {
            return (ReaderStoreStatus.Invalid, null);
        }

        using var guard = await EnterAsync(bookDir, ct).ConfigureAwait(false);
        var (state, _) = await LoadAsync<ProgressFile>(ProgressPath(bookDir), f => f.SchemaVersion, ct).ConfigureAwait(false);
        if (state == LoadState.Unsupported)
        {
            return (ReaderStoreStatus.UnsupportedVersion, null);
        }

        var file = new ProgressFile(ReaderSidecarWriter.SchemaVersion, chapterIndex, ms, DateTimeOffset.UtcNow);
        await WriteAtomicAsync(ProgressPath(bookDir), file, ct).ConfigureAwait(false);
        return (ReaderStoreStatus.Ok, file.ToProgress());
    }

    public async Task<IReadOnlyList<ReaderBookmark>> ListBookmarksAsync(string bookDir, CancellationToken ct)
    {
        using var guard = await EnterAsync(bookDir, ct).ConfigureAwait(false);
        var (state, file) = await LoadAsync<BookmarksFile>(BookmarksPath(bookDir), f => f.SchemaVersion, ct).ConfigureAwait(false);
        return state == LoadState.Ok ? ValidBookmarks(file!) : [];
    }

    public async Task<(ReaderStoreStatus Status, ReaderBookmark? Bookmark)> AddBookmarkAsync(
        string bookDir, int chapterIndex, long ms, string? note, string? excerpt, CancellationToken ct)
    {
        if (ms < 0 || note?.Length > MaxNoteLength || !await HasChapterAsync(bookDir, chapterIndex, ct).ConfigureAwait(false))
        {
            return (ReaderStoreStatus.Invalid, null);
        }

        var bookmark = new ReaderBookmark(
            Guid.NewGuid(), chapterIndex, ms, Truncate(excerpt, MaxExcerptLength), NullIfBlank(note), DateTimeOffset.UtcNow);
        var status = await MutateBookmarksAsync(bookDir, list => { list.Add(bookmark); return true; }, ct).ConfigureAwait(false);
        return (status, status == ReaderStoreStatus.Ok ? bookmark : null);
    }

    public async Task<(ReaderStoreStatus Status, ReaderBookmark? Bookmark)> UpdateBookmarkNoteAsync(
        string bookDir, Guid bookmarkId, string? note, CancellationToken ct)
    {
        if (note?.Length > MaxNoteLength)
        {
            return (ReaderStoreStatus.Invalid, null);
        }

        ReaderBookmark? updated = null;
        var status = await MutateBookmarksAsync(bookDir, list =>
        {
            var at = list.FindIndex(b => b.Id == bookmarkId);
            if (at < 0)
            {
                return false;
            }

            updated = list[at] with { Note = NullIfBlank(note) };
            list[at] = updated;
            return true;
        }, ct).ConfigureAwait(false);
        return (status, updated);
    }

    public Task<ReaderStoreStatus> DeleteBookmarkAsync(string bookDir, Guid bookmarkId, CancellationToken ct) =>
        MutateBookmarksAsync(bookDir, list => list.RemoveAll(b => b.Id == bookmarkId) > 0, ct);

    /// <summary>Applies <paramref name="mutate"/> to the list; returns NotFound (nothing written) when it reports no change.</summary>
    private async Task<ReaderStoreStatus> MutateBookmarksAsync(string bookDir, Func<List<ReaderBookmark>, bool> mutate, CancellationToken ct)
    {
        using var guard = await EnterAsync(bookDir, ct).ConfigureAwait(false);
        var (state, file) = await LoadAsync<BookmarksFile>(BookmarksPath(bookDir), f => f.SchemaVersion, ct).ConfigureAwait(false);
        if (state == LoadState.Unsupported)
        {
            return ReaderStoreStatus.UnsupportedVersion;
        }

        var list = state == LoadState.Ok ? ValidBookmarks(file!) : [];
        if (!mutate(list))
        {
            return ReaderStoreStatus.NotFound;
        }

        await WriteAtomicAsync(BookmarksPath(bookDir), new BookmarksFile(ReaderSidecarWriter.SchemaVersion, list), ct).ConfigureAwait(false);
        return ReaderStoreStatus.Ok;
    }

    private async Task<bool> HasChapterAsync(string bookDir, int chapterIndex, CancellationToken ct)
    {
        var index = await ReadIndexAsync(bookDir, ct).ConfigureAwait(false);
        return index?.Chapters?.Any(c => c.Index == chapterIndex) ?? false;
    }

    private async Task<(LoadState State, T? Value)> LoadAsync<T>(string path, Func<T, int> version, CancellationToken ct)
        where T : class
    {
        if (!File.Exists(path))
        {
            return (LoadState.Missing, null);
        }

        try
        {
            var bytes = await ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            var value = JsonSerializer.Deserialize<T>(bytes, JsonOptions);
            if (value is null)
            {
                return (LoadState.Corrupt, null);
            }

            return version(value) == ReaderSidecarWriter.SchemaVersion ? (LoadState.Ok, value) : (LoadState.Unsupported, null);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Ignoring malformed reader file {File}", path);
            return (LoadState.Corrupt, null);
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct)
    {
        // Share Delete so a concurrent File.Move over the file never fails because of an open reader.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + ".partial";
        await using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(partial, path, overwrite: true);
    }

    private async Task<Releaser> EnterAsync(string bookDir, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(Path.GetFullPath(bookDir), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new Releaser(gate);
    }

    /// <summary>Drops null entries a hand-edited file may contain, so they never reach callers or get rewritten.</summary>
    private static List<ReaderBookmark> ValidBookmarks(BookmarksFile file) =>
        (file.Bookmarks ?? []).Where(b => b is not null).ToList();

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? Truncate(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;

    private static string ReaderDir(string bookDir) => Path.Combine(bookDir, ReaderSidecarWriter.FolderName);

    private static string IndexPath(string bookDir) => Path.Combine(ReaderDir(bookDir), "index.json");

    private static string ProgressPath(string bookDir) => Path.Combine(ReaderDir(bookDir), "progress.json");

    private static string BookmarksPath(string bookDir) => Path.Combine(ReaderDir(bookDir), "bookmarks.json");

    private readonly struct Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    private sealed record ProgressFile(int SchemaVersion, int ChapterIndex, long Ms, DateTimeOffset UpdatedAt)
    {
        public ReaderProgress ToProgress() => new(ChapterIndex, Ms, UpdatedAt);
    }

    private sealed record BookmarksFile(int SchemaVersion, List<ReaderBookmark>? Bookmarks);
}
