using System.Security.Cryptography;
using ConversorLibros.Core.Services;

namespace ConversorLibros.Web.Services.Library;

public sealed record ImportBookResultDto(
    string FileName,
    string Status,
    string? BookId,
    string? Title,
    string? Reason);

/// <summary>
/// Adds uploaded <c>.epub</c> files directly into the configured <c>BooksRoot</c> folder (without creating a
/// second copy elsewhere), deduplicating by SHA-256 and validating the EPUB before moving it into place.
/// </summary>
public sealed class BookImportService(ILibraryContext context, SqliteLibraryRepository repository)
{
    public async Task<IReadOnlyList<ImportBookResultDto>> AddToFolderAsync(
        IReadOnlyList<(string FileName, Stream Content)> files,
        CancellationToken ct = default)
    {
        var booksRoot = context.BooksRoot;
        if (string.IsNullOrWhiteSpace(booksRoot) || !Directory.Exists(booksRoot))
        {
            throw new InvalidOperationException("Configura primero una carpeta de libros válida antes de añadir archivos.");
        }

        var fullBooksRoot = Path.GetFullPath(booksRoot);
        var tempRoot = Path.Combine(Path.GetTempPath(), "ConversorLibros", "import-staging");
        Directory.CreateDirectory(tempRoot);

        var epubSource = new EpubBookSource();
        var results = new List<ImportBookResultDto>(files.Count);

        foreach (var (rawFileName, content) in files)
        {
            ct.ThrowIfCancellationRequested();
            var displayName = Path.GetFileName(string.IsNullOrWhiteSpace(rawFileName) ? "libro.epub" : rawFileName);
            var tempFile = Path.Combine(tempRoot, $"{Guid.NewGuid():N}.epub");

            try
            {
                var sha256 = await CopyAndHashAsync(content, tempFile, ct).ConfigureAwait(false);

                var duplicate = await repository.FindBySha256Async(sha256, ct).ConfigureAwait(false);
                if (duplicate is not null)
                {
                    results.Add(new ImportBookResultDto(displayName, "skipped", duplicate.Id, duplicate.Title, "duplicate"));
                    continue;
                }

                // Validate EPUB before touching BooksRoot so corrupt uploads leave zero clutter.
                var parsed = await epubSource.LoadAsync(tempFile, ct).ConfigureAwait(false);

                var destPath = ChooseUniqueDestinationPath(fullBooksRoot, displayName);
                File.Move(tempFile, destPath, overwrite: false);
                tempFile = string.Empty;

                var lastWriteUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(destPath), TimeSpan.Zero);
                var bookId = Guid.NewGuid().ToString("N")[..12];
                var cacheDir = context.GetBookCacheDir(bookId);
                var coverPath = await TextOnlyReaderSidecarWriter.WriteCoverAsync(parsed, cacheDir, ct).ConfigureAwait(false);

                var record = LibraryFolderScanner.BuildBookRecord(
                    existing: null,
                    id: bookId,
                    book: parsed,
                    epubPath: destPath,
                    lastWriteUtc: lastWriteUtc,
                    sha256: sha256,
                    hasCover: coverPath is not null);

                var saved = await repository.UpsertBookAsync(record, parsed.Metadata?.Subjects ?? [], ct).ConfigureAwait(false);
                results.Add(new ImportBookResultDto(displayName, "imported", saved.Id, saved.Title, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new ImportBookResultDto(displayName, "error", null, null, ex.Message));
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { /* best-effort */ }
                }
            }
        }

        return results;
    }

    private static async Task<string> CopyAndHashAsync(Stream source, string destinationPath, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var dest = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
            await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }

    private static string ChooseUniqueDestinationPath(string booksRoot, string fileName)
    {
        var rawStem = Path.GetFileNameWithoutExtension(fileName);
        var safeStem = SanitizeFileName(string.IsNullOrWhiteSpace(rawStem) ? "libro" : rawStem);
        var candidate = Path.Combine(booksRoot, safeStem + ".epub");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 2; i < 10_000; i++)
        {
            candidate = Path.Combine(booksRoot, $"{safeStem} ({i}).epub");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(booksRoot, $"{safeStem}-{Guid.NewGuid():N}.epub");
    }

    private static string SanitizeFileName(string stem)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(stem.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length > 120 ? cleaned[..120].Trim() : cleaned;
    }
}
