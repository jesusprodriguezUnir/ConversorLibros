using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using ConversorLibros.Web.Models;

namespace ConversorLibros.Web.Services;

public sealed record ConversionJobRequest(
    string EpubPath,
    Book Book,
    IReadOnlySet<int> ChapterIndexes,
    float Speed,
    string? SourceLanguage,
    AudiobookFormat Format);

public sealed record ConversionRunResult(string? OutputPath, string? Warning);

public interface IConversionRunner
{
    Task<ConversionRunResult> RunAsync(
        ConversionJobRequest request,
        WebSettings settings,
        IProgress<ConversionProgress> progress,
        CancellationToken ct);
}

/// <summary>
/// Executes the Core conversion pipeline and audiobook packaging on a background thread.
/// Output layout: <c>&lt;OutputRoot&gt;/&lt;Title&gt;/</c> holds the final audio; <c>work/</c> holds chapter WAVs and caches.
/// </summary>
public sealed class CoreConversionRunner : IConversionRunner
{
    private const string Voice = "0";
    private const string TargetLanguage = "es";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly PiperModelLocator _locator;

    public CoreConversionRunner(PiperModelLocator locator)
    {
        _locator = locator;
    }

    public Task<ConversionRunResult> RunAsync(
        ConversionJobRequest request,
        WebSettings settings,
        IProgress<ConversionProgress> progress,
        CancellationToken ct) =>
        Task.Run(() => RunCoreAsync(request, settings, progress, ct), ct);

    private async Task<ConversionRunResult> RunCoreAsync(
        ConversionJobRequest request,
        WebSettings settings,
        IProgress<ConversionProgress> progress,
        CancellationToken ct)
    {
        var piper = _locator.Resolve(settings.PiperModelDir);
        if (!piper.Status.IsValid)
        {
            throw new InvalidOperationException($"Modelo Piper no válido en Ajustes: {piper.Status.Message}");
        }

        var modelDir = piper.EffectiveDir;
        var onnxPath = Directory.GetFiles(modelDir, "*.onnx").First();

        var layout = BookOutputLayout.For(settings.OutputRoot, request.Book.Title);
        Directory.CreateDirectory(layout.WorkDir);

        var translate = !string.IsNullOrWhiteSpace(request.SourceLanguage);
        CachedChapterTranslator? translator = translate
            ? new CachedChapterTranslator(
                new OllamaTranslator(_http, settings.OllamaUrl, settings.OllamaModel),
                new FileSystemTranslationCache(layout.TranslationCacheDir))
            : null;

        using var tts = new PiperSherpaTtsEngine(
            onnxPath,
            Path.Combine(modelDir, "tokens.txt"),
            Path.Combine(modelDir, "espeak-ng-data"),
            Path.GetFileNameWithoutExtension(onnxPath));

        var pipeline = new BookConversionPipeline(
            new EpubBookSource(),
            new SpanishTextNormalizer(),
            new SentenceChunker(),
            tts,
            new FileSystemChunkCache(layout.CacheDir),
            new WavChapterAssembler(),
            translator);

        var syncMaps = new List<ChapterSyncMap>();
        var chapterWavs = await pipeline.ConvertAsync(
            request.EpubPath,
            layout.WorkDir,
            Voice,
            request.Speed,
            progress: progress,
            cancellationToken: ct,
            sourceLanguage: request.SourceLanguage,
            targetLanguage: translate ? TargetLanguage : null,
            chapterIndexes: request.ChapterIndexes,
            onChapterSynced: syncMaps.Add);

        ct.ThrowIfCancellationRequested();

        var ffmpeg = JsonSettingsStore.ResolveFfmpeg(settings.FfmpegOverride).ResolvedPath;
        if (ffmpeg is null)
        {
            return new ConversionRunResult(
                null,
                $"ffmpeg no encontrado: los WAV de capítulo están en {layout.WorkDir}. Instálalo con 'winget install Gyan.FFmpeg' o indica su ruta en Ajustes.");
        }

        var chapters = await AudiobookChapterResolver.ResolveAsync(
            request.Book,
            chapterWavs,
            translator,
            request.SourceLanguage,
            TargetLanguage,
            ct);

        progress.Report(new ConversionProgress(0, 0, 0, 0, "Exporting"));

        // Opus exporter appends the sanitized title to its root; the m4b exporter writes into the book folder itself.
        // Both sanitize the same book.Title, so both land in layout.BookDir.
        var isM4b = request.Format == AudiobookFormat.M4b;
        IAudiobookExporter exporter = isM4b
            ? new M4bExporter(ffmpeg)
            : new OpusFolderExporter(ffmpeg);
        var exportRoot = isM4b ? layout.BookDir : settings.OutputRoot;

        try
        {
            return await ReaderDataPublisher.ExportThenPublishAsync(
                exporter, request.Book, chapters, exportRoot, syncMaps, chapterWavs.Count, layout.BookDir, request.Format, ct);
        }
        catch (FfmpegNotFoundException)
        {
            return new ConversionRunResult(null, $"ffmpeg no es ejecutable en '{ffmpeg}'. Los WAV están en {layout.WorkDir}.");
        }
    }
}
