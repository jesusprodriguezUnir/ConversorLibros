using ConversorLibros.Core.Services;

namespace ConversorLibros.Web.Services;

/// <summary>
/// One folder per book. Final audio (Opus chapters + cover, or a single .m4b) lives directly in
/// <see cref="BookDir"/>; pipeline output and caches live under <see cref="WorkDir"/>.
/// </summary>
public sealed record BookOutputLayout(string BookDir, string WorkDir, string CacheDir, string TranslationCacheDir)
{
    public const string WorkFolderName = "work";

    public static BookOutputLayout For(string outputRoot, string bookTitle)
    {
        var bookDir = Path.Combine(outputRoot, AudiobookFileNames.SanitizeComponent(bookTitle));
        var workDir = Path.Combine(bookDir, WorkFolderName);
        return new BookOutputLayout(
            BookDir: bookDir,
            WorkDir: workDir,
            CacheDir: Path.Combine(workDir, "cache"),
            TranslationCacheDir: Path.Combine(workDir, "translation-cache"));
    }
}
