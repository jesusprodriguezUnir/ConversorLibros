using ConversorLibros.Core.Abstractions;

namespace ConversorLibros.Web.Services.Library;

/// <summary>
/// Non-destructively registers existing converted audiobooks under <c>OutputRoot</c> into SQLite,
/// preserving their legacy <see cref="LibraryIds.Compute"/> identifiers and never moving files on disk.
/// </summary>
public sealed class LegacyLibraryImporter(ILibraryContext context, SqliteLibraryRepository repository)
{
    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        var outputRoot = context.OutputRoot;
        if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot))
        {
            return 0;
        }

        var cacheDir = Path.GetFullPath(Path.Combine(outputRoot, LibraryContext.CacheFolderName));
        var scanned = LibraryScanner.Scan(outputRoot);
        var added = 0;

        foreach (var item in scanned)
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = Path.GetFullPath(item.Path);
            if (fullPath.StartsWith(cacheDir, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var id = LibraryIds.Compute(item.Path);
            var existing = await repository.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(outputRoot, fullPath);
            var format = item.Format == AudiobookFormat.M4b ? "m4b" : "opus";
            var durationMs = item.Duration.HasValue ? (long?)Math.Round(item.Duration.Value.TotalMilliseconds) : null;

            var record = new BookRecord
            {
                Id = id,
                Title = item.Title,
                SortTitle = item.Title,
                Author = string.IsNullOrWhiteSpace(item.Author) ? null : item.Author,
                FolderPath = relativePath,
                IsLegacy = true,
                HasEpub = false,
                HasCover = !string.IsNullOrWhiteSpace(item.CoverPath) && File.Exists(item.CoverPath),
                AudioStatus = "ready",
                AudioFormat = format,
                AudioDurationMs = durationMs,
                ChapterCount = item.ChapterCount,
            };

            await repository.UpsertBookAsync(record, [], ct).ConfigureAwait(false);
            added++;
        }

        return added;
    }
}
