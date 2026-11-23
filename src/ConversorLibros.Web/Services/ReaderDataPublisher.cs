using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;

namespace ConversorLibros.Web.Services;

/// <summary>
/// Post-export step of a conversion: turns the in-memory sync maps into reader sidecars. Reader data is auxiliary,
/// so any failure except cancellation becomes a warning and never breaks an already-exported audiobook.
/// </summary>
public static class ReaderDataPublisher
{
    public const string Warning = "Audiolibro creado, pero no se pudieron generar los datos de lectura.";

    private static readonly SpanishTextNormalizer Normalizer = new();

    /// <summary>
    /// Exports the audiobook and only then publishes reader data, so sidecars never sit next to stale or missing audio.
    /// Export failures propagate untouched; reader failures become a warning on the successful result.
    /// </summary>
    public static async Task<ConversionRunResult> ExportThenPublishAsync(
        IAudiobookExporter exporter,
        Book book,
        IReadOnlyList<AudiobookChapter> chapters,
        string exportRoot,
        IReadOnlyList<ChapterSyncMap> maps,
        int expectedChapters,
        string bookDir,
        AudiobookFormat format,
        CancellationToken ct)
    {
        var outputPath = await exporter.ExportAsync(book, chapters, exportRoot, ct).ConfigureAwait(false);
        var readerWarning = await PublishAsync(maps, expectedChapters, book.Title, bookDir, format, ct).ConfigureAwait(false);
        return new ConversionRunResult(outputPath, readerWarning);
    }

    /// <param name="expectedChapters">Chapters the pipeline assembled; fewer maps means some chapter could not be timed.</param>
    /// <returns>A warning to surface, or null when nothing went wrong (or the format has no reader support).</returns>
    public static async Task<string?> PublishAsync(
        IReadOnlyList<ChapterSyncMap> maps,
        int expectedChapters,
        string bookTitle,
        string bookDir,
        AudiobookFormat format,
        CancellationToken ct)
    {
        if (format != AudiobookFormat.Opus)
        {
            return null;
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            // Same normalizer and (null) dictionary as the runner's pipeline call, so normalized sentences match the chunks.
            var chapters = maps
                .Select(m => new ReaderChapter(m.ChapterIndex, m.Title, m.DurationMs, m.Chunks, ReaderTextAligner.Align(m, Normalizer, null)))
                .ToList();

            if (chapters.Count > 0)
            {
                await ReaderSidecarWriter.WriteAsync(bookDir, bookTitle, chapters, ct).ConfigureAwait(false);
            }

            return maps.Count < expectedChapters ? Warning : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Warning;
        }
    }
}
