using System.Security.Cryptography;
using ConversorLibros.Core.Services;

namespace ConversorLibros.Web.Services.Library;

public sealed record FolderScanSummary(
    int Added,
    int Updated,
    int Unchanged,
    int Removed,
    IReadOnlyList<string> Errors);

/// <summary>
/// Recursively scans <see cref="ILibraryContext.BooksRoot"/> for DRM-free <c>.epub</c> files and indexes them
/// in-place in SQLite without copying or modifying the original files.
/// </summary>
public sealed class LibraryFolderScanner(ILibraryContext context, SqliteLibraryRepository repository)
{
    public async Task<FolderScanSummary> ScanAsync(CancellationToken ct = default)
    {
        var booksRoot = context.BooksRoot;
        if (string.IsNullOrWhiteSpace(booksRoot) || !Directory.Exists(booksRoot))
        {
            return new FolderScanSummary(0, 0, 0, 0, []);
        }

        var fullRoot = Path.GetFullPath(booksRoot);
        var epubFiles = EnumerateEpubFilesSafe(fullRoot);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        int added = 0, updated = 0, unchanged = 0, removed = 0;

        var epubSource = new EpubBookSource();

        foreach (var epubPath in epubFiles)
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(epubPath);
            seenPaths.Add(fullPath);

            try
            {
                var lastWriteUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero);
                var existing = await repository.FindByEpubPathAsync(fullPath, ct).ConfigureAwait(false);
                if (existing is not null
                    && existing.EpubLastWriteUtc.HasValue
                    && Math.Abs((existing.EpubLastWriteUtc.Value - lastWriteUtc).TotalSeconds) < 1)
                {
                    unchanged++;
                    continue;
                }

                var sha256 = await ComputeSha256Async(fullPath, ct).ConfigureAwait(false);
                var book = await epubSource.LoadAsync(fullPath, ct).ConfigureAwait(false);

                var id = existing?.Id ?? Guid.NewGuid().ToString("N")[..12];
                var cacheDir = context.GetBookCacheDir(id);
                var coverPath = await TextOnlyReaderSidecarWriter.WriteCoverAsync(book, cacheDir, ct).ConfigureAwait(false);

                var record = BuildBookRecord(existing, id, book, fullPath, lastWriteUtc, sha256, coverPath is not null);
                var tags = existing is not null && existing.Tags.Count > 0
                    ? existing.Tags
                    : (book.Metadata?.Subjects ?? []);

                await repository.UpsertBookAsync(record, tags, ct).ConfigureAwait(false);
                if (existing is null)
                {
                    added++;
                }
                else
                {
                    updated++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{Path.GetFileName(fullPath)}: {ex.Message}");
            }
        }

        // Reconcile books whose original EPUB under BooksRoot was deleted from disk.
        var rootWithSep = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
        var allBooks = await repository.ListAllAsync(ct).ConfigureAwait(false);
        foreach (var existing in allBooks)
        {
            if (existing.IsLegacy || string.IsNullOrWhiteSpace(existing.EpubPath))
            {
                continue;
            }

            var existingFull = Path.GetFullPath(existing.EpubPath);
            if (!existingFull.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seenPaths.Contains(existingFull))
            {
                continue;
            }

            if (string.Equals(existing.AudioStatus, "none", StringComparison.OrdinalIgnoreCase))
            {
                await repository.DeleteBookAsync(existing.Id, ct).ConfigureAwait(false);
                removed++;
            }
            else
            {
                await repository.UpsertBookAsync(
                    existing with { HasEpub = false, EpubPath = null, EpubLastWriteUtc = null, UpdatedAt = DateTimeOffset.UtcNow },
                    existing.Tags,
                    ct).ConfigureAwait(false);
            }
        }

        return new FolderScanSummary(added, updated, unchanged, removed, errors);
    }

    internal static BookRecord BuildBookRecord(
        BookRecord? existing,
        string id,
        Core.Models.Book book,
        string epubPath,
        DateTimeOffset lastWriteUtc,
        string sha256,
        bool hasCover)
    {
        var title = string.IsNullOrWhiteSpace(book.Title)
            ? Path.GetFileNameWithoutExtension(epubPath)
            : book.Title.Trim();
        var meta = book.Metadata;

        return new BookRecord
        {
            Id = id,
            Title = existing?.Title is { Length: > 0 } ? existing.Title : title,
            SortTitle = existing?.SortTitle is { Length: > 0 } ? existing.SortTitle : title,
            Author = existing?.Author ?? (string.IsNullOrWhiteSpace(book.Author) ? null : book.Author.Trim()),
            Series = existing?.Series ?? meta?.Series,
            SeriesIndex = existing?.SeriesIndex ?? meta?.SeriesIndex,
            Publisher = existing?.Publisher ?? meta?.Publisher,
            PublishedDate = existing?.PublishedDate ?? meta?.PublishedDate,
            Isbn = existing?.Isbn ?? meta?.Isbn,
            Description = existing?.Description ?? meta?.Description,
            Language = existing?.Language ?? (string.IsNullOrWhiteSpace(book.Language) ? null : book.Language.Trim()),
            EpubPath = epubPath,
            EpubLastWriteUtc = lastWriteUtc,
            SourceSha256 = sha256,
            FolderPath = existing?.FolderPath,
            IsLegacy = false,
            HasEpub = true,
            HasCover = hasCover || (existing?.HasCover ?? false),
            AudioStatus = existing?.AudioStatus ?? "none",
            AudioFormat = existing?.AudioFormat,
            AudioDurationMs = existing?.AudioDurationMs,
            ChapterCount = book.Chapters.Count,
            VoiceUsed = existing?.VoiceUsed,
            ReadingChapter = existing?.ReadingChapter,
            ReadingPercent = existing?.ReadingPercent ?? 0,
            LastReadAt = existing?.LastReadAt,
            CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    internal static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static List<string> EnumerateEpubFilesSafe(string root)
    {
        var results = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.epub"))
                {
                    results.Add(file);
                }

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (name.StartsWith('.'))
                    {
                        continue;
                    }

                    pending.Push(sub);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Skip inaccessible folders without failing the whole scan.
            }
        }

        return results;
    }
}
