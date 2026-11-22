using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Writes <c>{outputRoot}/{Book title}/</c> with one <c>NNN - Chapter.opus</c> per chapter (tagged) and a
/// <c>cover.jpg</c>/<c>cover.png</c> taken from the EPUB. Chapter files are encoded to a temporary name first,
/// so a failed encode never replaces a previous good export.
/// </summary>
public sealed class OpusFolderExporter : IAudiobookExporter
{
    private const string PartialSuffix = ".partial";

    private readonly string _ffmpegPath;

    public OpusFolderExporter(string ffmpegPath)
    {
        _ffmpegPath = string.IsNullOrWhiteSpace(ffmpegPath)
            ? throw new ArgumentException("ffmpeg path cannot be empty.", nameof(ffmpegPath))
            : ffmpegPath;
    }

    public AudiobookFormat Format => AudiobookFormat.Opus;

    public async Task<string> ExportAsync(
        Book book,
        IReadOnlyList<AudiobookChapter> chapters,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(chapters);
        if (chapters.Count == 0)
        {
            throw new ArgumentException("At least one chapter is required.", nameof(chapters));
        }

        var folder = Path.Combine(outputRoot, AudiobookFileNames.SanitizeComponent(book.Title));
        Directory.CreateDirectory(folder);

        foreach (var chapter in chapters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = Path.Combine(folder, AudiobookFileNames.ChapterFileName(chapter.Index, chapter.Title));
            var partial = target + PartialSuffix;
            try
            {
                await FfmpegProcess.RunAsync(
                        _ffmpegPath,
                        FfmpegCommands.OpusChapter(
                            chapter.AudioPath,
                            partial,
                            chapter.Title,
                            book.Author,
                            book.Title,
                            chapter.Index),
                        cancellationToken)
                    .ConfigureAwait(false);

                // Remove earlier exports of this chapter (e.g. the same chapter under a different title).
                foreach (var stale in Directory.EnumerateFiles(folder, AudiobookFileNames.ChapterPrefix(chapter.Index) + "*.opus").ToList())
                {
                    File.Delete(stale);
                }

                File.Move(partial, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }
            }
        }

        if (book.CoverImage is { Length: > 0 })
        {
            WriteCover(folder, book);
        }

        return folder;
    }

    private static void WriteCover(string folder, Book book)
    {
        foreach (var existing in Directory.EnumerateFiles(folder, "cover.*").ToList())
        {
            File.Delete(existing);
        }

        var extension = book.CoverMimeType?.ToLowerInvariant() == "image/png" ? ".png" : ".jpg";
        File.WriteAllBytes(Path.Combine(folder, "cover" + extension), book.CoverImage!);
    }
}
