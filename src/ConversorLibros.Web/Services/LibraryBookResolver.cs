namespace ConversorLibros.Web.Services;

/// <summary>
/// Resolves a library id to an Opus book folder without a full library scan (audio Range and progress requests
/// are frequent). Ids match <see cref="LibraryIds.Compute"/> over the same <c>EnumerateDirectories</c> paths the
/// scanner uses, so they stay identical to the listing ids.
/// </summary>
public sealed class LibraryBookResolver(JsonSettingsStore settingsStore)
{
    /// <summary>Returns the book folder, or null when the id is unknown, not Opus, or not under OutputRoot.</summary>
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

    public string OutputRoot => settingsStore.Get().OutputRoot;
}
