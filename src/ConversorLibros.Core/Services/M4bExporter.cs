using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Packages all chapters into <c>{outputRoot}/{Book title}.m4b</c> (AAC mono 64 kbps, chapter markers, cover).
/// Intermediate files live in a per-run temp folder that is always removed.
/// </summary>
public sealed class M4bExporter : IAudiobookExporter
{
    private readonly string _ffmpegPath;

    public M4bExporter(string ffmpegPath)
    {
        _ffmpegPath = string.IsNullOrWhiteSpace(ffmpegPath)
            ? throw new ArgumentException("ffmpeg path cannot be empty.", nameof(ffmpegPath))
            : ffmpegPath;
    }

    public AudiobookFormat Format => AudiobookFormat.M4b;

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

        Directory.CreateDirectory(outputRoot);
        var outputPath = Path.Combine(outputRoot, AudiobookFileNames.SanitizeComponent(book.Title) + ".m4b");
        var workDir = Path.Combine(Path.GetTempPath(), "conversorlibros-m4b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            var concatPath = Path.Combine(workDir, "concat.txt");
            var metadataPath = Path.Combine(workDir, "ffmetadata.txt");
            var encodedPath = Path.Combine(workDir, "audiobook.m4b");

            var concatLines = chapters.Select(c => $"file '{Path.GetFullPath(c.AudioPath).Replace("'", "'\\''")}'");
            await File.WriteAllLinesAsync(concatPath, concatLines, cancellationToken).ConfigureAwait(false);

            var durations = chapters
                .Select(c => (c.Title, WavFile.ReadDuration(c.AudioPath)))
                .ToList();
            await File.WriteAllTextAsync(metadataPath, FfmetadataBuilder.Build(book.Title, book.Author, durations), cancellationToken)
                .ConfigureAwait(false);

            string? coverPath = null;
            if (book.CoverImage is { Length: > 0 })
            {
                coverPath = Path.Combine(workDir, "cover" + (book.CoverMimeType?.ToLowerInvariant() == "image/png" ? ".png" : ".jpg"));
                await File.WriteAllBytesAsync(coverPath, book.CoverImage, cancellationToken).ConfigureAwait(false);
            }

            await FfmpegProcess.RunAsync(
                    _ffmpegPath,
                    FfmpegCommands.M4bPackage(concatPath, metadataPath, coverPath, encodedPath),
                    cancellationToken)
                .ConfigureAwait(false);

            File.Move(encodedPath, outputPath, overwrite: true);
            return outputPath;
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
