using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;

namespace ConversorLibros.App.Services;

public sealed record ConversionRequest(
    string EpubPath,
    Book Book,
    IReadOnlySet<int> ChapterIndexes,
    float Speed,
    string? SourceLanguage,
    AudiobookFormat Format);

/// <summary>OutputPath is null when packaging was skipped; Warning then explains why.</summary>
public sealed record ConversionResult(string? OutputPath, string? Warning);

/// <summary>
/// Runs the Core pipeline and packages its output. Piper synthesis is synchronous,
/// so the whole run executes on a thread-pool thread.
/// </summary>
public sealed class ConversionService
{
    private const string Voice = "0";
    private const string TargetLanguage = "es";

    // Generous timeout: a local 8B model can take minutes per long paragraph on CPU.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static string? ResolveFfmpeg(string? overridePath) =>
        !string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)
            ? overridePath
            : FfmpegLocator.Locate();

    public Task<ConversionResult> RunAsync(
        ConversionRequest request,
        AppSettings settings,
        IProgress<ConversionProgress> progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => RunCoreAsync(request, settings, progress, cancellationToken), cancellationToken);

    private async Task<ConversionResult> RunCoreAsync(
        ConversionRequest request,
        AppSettings settings,
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

        var translate = request.SourceLanguage is not null;
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

        var ffmpeg = ResolveFfmpeg(settings.FfmpegOverride);
        if (ffmpeg is null)
        {
            return new ConversionResult(
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
            return new ConversionResult(outputPath, null);
        }
        catch (FfmpegNotFoundException)
        {
            return new ConversionResult(null, $"ffmpeg no es ejecutable en '{ffmpeg}'. Los WAV están en {outDir}.");
        }
    }
}
