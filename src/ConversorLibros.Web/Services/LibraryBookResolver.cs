using ConversorLibros.Web.Services.Library;

namespace ConversorLibros.Web.Services;

/// <summary>
/// Resolves a library id to a book directory containing <c>reader/</c> sidecars (either a legacy Opus folder
/// under OutputRoot or a SQLite-cataloged EPUB book whose text-only sidecars live in <c>.library-cache/&lt;id&gt;</c>).
/// </summary>
public sealed class LibraryBookResolver(
    JsonSettingsStore settingsStore,
    ILibraryContext libraryContext,
    SqliteLibraryRepository repository,
    TextOnlyReaderSidecarWriter textOnlyWriter)
{
    /// <summary>Returns the legacy Opus book folder, or null when unknown, not Opus, or not under OutputRoot.</summary>
    public string? ResolveOpusBookDir(string id)
    {
        var outputRoot = settingsStore.Get().OutputRoot;
        if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot))
        {
            return null;
        }

        foreach (var folder in Directory.EnumerateDirectories(outputRoot))
        {
            if (LibraryIds.Compute(folder) != id)
            {
                continue;
            }

            // The reader folder is checked too: a junction there would expose files outside OutputRoot.
            return LibraryScanner.IsInside(outputRoot, folder)
                   && LibraryScanner.IsInside(outputRoot, Path.Combine(folder, ReaderSidecarWriter.FolderName))
                   && Directory.EnumerateFiles(folder, "*.opus").Any()
                ? folder
                : null;
        }

        return null;
    }

    /// <summary>
    /// Resolves any readable book directory (legacy Opus or cataloged EPUB). For cataloged EPUBs without
    /// generated sidecars yet, generates text-only sidecars lazily on first open.
    /// </summary>
    public async Task<string?> ResolveReaderDirAsync(string id, CancellationToken ct = default)
    {
        if (ResolveOpusBookDir(id) is { } opusDir)
        {
            return opusDir;
        }

        var book = await repository.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (book is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(book.FolderPath))
        {
            var candidate = Path.IsPathRooted(book.FolderPath)
                ? book.FolderPath
                : Path.Combine(libraryContext.OutputRoot, book.FolderPath);
            if (Directory.Exists(candidate)
                && LibraryScanner.IsInside(libraryContext.OutputRoot, candidate)
                && File.Exists(Path.Combine(candidate, ReaderSidecarWriter.FolderName, "index.json")))
            {
                return candidate;
            }
        }

        if (book.HasEpub)
        {
            var cacheDir = libraryContext.GetBookCacheDir(book.Id);
            if (await textOnlyWriter.EnsureGeneratedAsync(book, cacheDir, ct).ConfigureAwait(false))
            {
                return cacheDir;
            }
        }

        return null;
    }

    public string OutputRoot => settingsStore.Get().OutputRoot;
}
