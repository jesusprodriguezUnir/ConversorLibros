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
/// </summary>
public sealed class CoreConversionRunner : IConversionRunner
{
    private const string Voice = "0";
    private const string TargetLanguage = "es";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

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
        var modelDir = settings.PiperModelDir;
        var onnxPath = string.IsNullOrWhiteSpace(modelDir) || !Directory.Exists(modelDir)
            ? null
            : Directory.GetFiles(modelDir, "*.onnx").FirstOrDefault();
        if (onnxPath is null)
        {
            throw new InvalidOperationException("Falta la carpeta del modelo Piper (con un .onnx) en Ajustes.");
        }

        var outDir = settings.OutputRoot;
        Directory.CreateDirectory(outDir);

        var translate = !string.IsNullOrWhiteSpace(request.SourceLanguage);
        CachedChapterTranslator? translator = translate
            ? new CachedChapterTranslator(
                new OllamaTranslator(_http, settings.OllamaUrl, settings.OllamaModel),
                new FileSystemTranslationCache(Path.Combine(outDir, "translation-cache")))
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
            new FileSystemChunkCache(Path.Combine(outDir, "cache")),
            new WavChapterAssembler(),
            translator);

        var chapterWavs = await pipeline.ConvertAsync(
            request.EpubPath,
            outDir,
            Voice,
            request.Speed,
            progress: progress,
            cancellationToken: ct,
            sourceLanguage: request.SourceLanguage,
            targetLanguage: translate ? TargetLanguage : null,
            chapterIndexes: request.ChapterIndexes);

        ct.ThrowIfCancellationRequested();

        var ffmpeg = JsonSettingsStore.ResolveFfmpeg(settings.FfmpegOverride).ResolvedPath;
        if (ffmpeg is null)
        {
            return new ConversionRunResult(
                null,
                $"ffmpeg no encontrado: los WAV de capítulo están en {outDir}. Instálalo con 'winget install Gyan.FFmpeg' o indica su ruta en Ajustes.");
        }

        var chapters = await AudiobookChapterResolver.ResolveAsync(
            request.Book,
            chapterWavs,
            translator,
            request.SourceLanguage,
            TargetLanguage,
            ct);

        progress.Report(new ConversionProgress(0, 0, 0, 0, "Exporting"));

        IAudiobookExporter exporter = request.Format == AudiobookFormat.M4b
            ? new M4bExporter(ffmpeg)
            : new OpusFolderExporter(ffmpeg);

        try
        {
            var outputPath = await exporter.ExportAsync(request.Book, chapters, outDir, ct);
            return new ConversionRunResult(outputPath, null);
        }
        catch (FfmpegNotFoundException)
        {
            return new ConversionRunResult(null, $"ffmpeg no es ejecutable en '{ffmpeg}'. Los WAV están en {outDir}.");
        }
    }
}
