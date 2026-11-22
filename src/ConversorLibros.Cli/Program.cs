using System.Text;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Services;

namespace ConversorLibros.Cli;

internal static class Program
{
    private const string DefaultOllamaUrl = "http://localhost:11434";
    private const string DefaultOllamaModel = "qwen3:8b";
    private const string DefaultTranslateTo = "es";
    private const string DefaultFormat = "opus";

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "convert", StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 1;
        }

        var epubPath = args[1];
        var voice = GetOption(args, "--voice") ?? "0";
        var speed = float.TryParse(GetOption(args, "--speed"), out var s) ? s : 1.0f;
        var outDir = GetOption(args, "--out") ?? Path.Combine(Directory.GetCurrentDirectory(), "out");
        var modelDir = GetOption(args, "--model-dir");
        var chaptersRange = GetOption(args, "--chapters");
        var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
        var deleteWav = args.Contains("--delete-wav", StringComparer.OrdinalIgnoreCase);
        var translateFrom = GetOption(args, "--translate-from");
        var translateTo = GetOption(args, "--translate-to") ?? DefaultTranslateTo;
        var ollamaUrl = GetOption(args, "--ollama-url") ?? DefaultOllamaUrl;
        var ollamaModel = GetOption(args, "--ollama-model") ?? DefaultOllamaModel;
        var formatOption = GetOption(args, "--format") ?? DefaultFormat;

        if (!TryParseFormat(formatOption, out var format))
        {
            Console.Error.WriteLine($"Formato no válido: '{formatOption}'. Valores admitidos: opus, m4b.");
            PrintUsage();
            return 1;
        }

        var (startChapter, endChapter) = ParseChapterRange(chaptersRange);

        var bookSource = new EpubBookSource();
        var normalizer = new SpanishTextNormalizer();
        var chunker = new SentenceChunker();

        // Generous timeout: a local 8B model can take minutes per long paragraph on CPU.
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        CachedChapterTranslator? chapterTranslator = null;
        if (!string.IsNullOrWhiteSpace(translateFrom))
        {
            var translator = new OllamaTranslator(httpClient, ollamaUrl, ollamaModel);
            var translationCache = new FileSystemTranslationCache(Path.Combine(outDir, "translation-cache"));
            chapterTranslator = new CachedChapterTranslator(translator, translationCache);
        }

        var book = await bookSource.LoadAsync(epubPath);
        var selected = book.Chapters
            .Where(c => (!startChapter.HasValue || c.Index >= startChapter.Value)
                     && (!endChapter.HasValue || c.Index <= endChapter.Value))
            .ToList();

        Console.WriteLine($"Libro: {book.Title} ({book.Author}) — {selected.Count} capítulo(s) seleccionado(s).");

        if (dryRun)
        {
            Directory.CreateDirectory(outDir);
            var dryRunPath = Path.Combine(outDir, "dry-run.txt");
            var sb = new StringBuilder();

            foreach (var sourceChapter in selected)
            {
                var chapter = sourceChapter;
                if (chapterTranslator is not null)
                {
                    chapter = await chapterTranslator.TranslateChapterAsync(
                        sourceChapter,
                        translateFrom!,
                        translateTo,
                        (done, total) => Console.WriteLine($"[Cap {sourceChapter.Index}] Traduciendo ({done}/{total})"));
                }

                sb.AppendLine($"=== Capítulo {chapter.Index}: {chapter.Title} ===");
                var normalized = normalizer.Normalize(chapter.RawText);
                var chunks = chunker.Chunk(chapter, normalized, voice, speed, "dry-run");
                foreach (var chunk in chunks)
                {
                    sb.AppendLine($"[{chunk.Order:D3}] {chunk.NormalizedText}");
                }
                sb.AppendLine();
            }

            await File.WriteAllTextAsync(dryRunPath, sb.ToString());
            Console.WriteLine($"Dry-run guardado en: {dryRunPath}");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(modelDir) || !Directory.Exists(modelDir))
        {
            Console.Error.WriteLine("Falta --model-dir <carpeta_modelo_piper> (con .onnx, tokens.txt y espeak-ng-data).");
            return 2;
        }

        var onnxPath = Directory.GetFiles(modelDir, "*.onnx").FirstOrDefault();
        var tokensPath = Path.Combine(modelDir, "tokens.txt");
        var dataDir = Path.Combine(modelDir, "espeak-ng-data");
        if (onnxPath is null)
        {
            Console.Error.WriteLine($"No se encontró ningún archivo .onnx en '{modelDir}'.");
            return 2;
        }

        using var tts = new PiperSherpaTtsEngine(onnxPath, tokensPath, dataDir, Path.GetFileNameWithoutExtension(onnxPath));
        var cache = new FileSystemChunkCache(Path.Combine(outDir, "cache"));
        var assembler = new WavChapterAssembler();
        var pipeline = new BookConversionPipeline(bookSource, normalizer, chunker, tts, cache, assembler, chapterTranslator);

        var progress = new Progress<ConversorLibros.Core.Models.ConversionProgress>(p =>
            Console.WriteLine($"[Cap {p.ChapterIndex}/{p.TotalChapters}] {p.Stage} ({p.CompletedChunks}/{p.TotalChunks})"));

        var chapterWavs = await pipeline.ConvertAsync(
            epubPath,
            outDir,
            voice,
            speed,
            startChapter,
            endChapter,
            progress: progress,
            sourceLanguage: translateFrom,
            targetLanguage: translateFrom is null ? null : translateTo);

        var ffmpegPath = FfmpegLocator.Locate();
        if (ffmpegPath is null)
        {
            // Chapter WAVs are still valid; only the audiobook packaging is skipped.
            Console.Error.WriteLine("Aviso: no se encontró ffmpeg (PATH ni WinGet); no se empaqueta el audiolibro.");
            Console.Error.WriteLine("Instálalo con 'winget install Gyan.FFmpeg' o indica su ruta en los ajustes de la app.");
            Console.WriteLine($"Capítulos WAV generados en: {Path.GetFullPath(outDir)}");
            return 0;
        }

        var chapters = await AudiobookChapterResolver.ResolveAsync(
            book,
            chapterWavs,
            chapterTranslator,
            translateFrom,
            translateTo);

        IAudiobookExporter exporter = format == AudiobookFormat.M4b
            ? new M4bExporter(ffmpegPath)
            : new OpusFolderExporter(ffmpegPath);

        string outputPath;
        try
        {
            outputPath = await exporter.ExportAsync(book, chapters, outDir);
        }
        catch (FfmpegNotFoundException)
        {
            Console.Error.WriteLine($"Aviso: ffmpeg no es ejecutable en '{ffmpegPath}'; no se empaqueta el audiolibro.");
            Console.WriteLine($"Capítulos WAV generados en: {Path.GetFullPath(outDir)}");
            return 0;
        }

        if (deleteWav)
        {
            // The chunk cache stays, so regenerating a chapter WAV later only re-assembles it.
            foreach (var wavPath in chapterWavs)
            {
                File.Delete(wavPath);
            }
        }

        Console.WriteLine($"Audiolibro generado ({format.ToString().ToLowerInvariant()}): {outputPath}");
        return 0;
    }

    private static bool TryParseFormat(string value, out AudiobookFormat format)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "opus":
                format = AudiobookFormat.Opus;
                return true;
            case "m4b":
                format = AudiobookFormat.M4b;
                return true;
            default:
                format = AudiobookFormat.Opus;
                return false;
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static (int? Start, int? End) ParseChapterRange(string? range)
    {
        if (string.IsNullOrWhiteSpace(range))
        {
            return (null, null);
        }

        var parts = range.Split('-', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out var single))
        {
            return (single, single);
        }

        if (parts.Length == 2)
        {
            int? start = int.TryParse(parts[0], out var s) ? s : null;
            int? end = int.TryParse(parts[1], out var e) ? e : null;
            return (start, end);
        }

        return (null, null);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Uso: conversor convert <libro.epub> [opciones]");
        Console.WriteLine();
        Console.WriteLine("Opciones:");
        Console.WriteLine("  --voice <id>            Id numérico de speaker de Piper (por defecto: 0).");
        Console.WriteLine("  --speed <n>             Velocidad de lectura (por defecto: 1.0).");
        Console.WriteLine("  --out <carpeta>         Carpeta de salida y caché (por defecto: ./out).");
        Console.WriteLine("  --chapters <rango>      Capítulos a convertir, p. ej. 1-3 o 5.");
        Console.WriteLine("  --model-dir <carpeta>   Modelo Piper (.onnx, tokens.txt, espeak-ng-data). Obligatorio sin --dry-run.");
        Console.WriteLine("  --dry-run               Genera out/dry-run.txt con el texto normalizado, sin sintetizar audio.");
        Console.WriteLine("  --format <opus|m4b>     Formato del audiolibro (por defecto: opus). opus = carpeta con un .opus por capítulo; m4b = un solo fichero.");
        Console.WriteLine("  --delete-wav            Borra los WAV de capítulo tras exportar el audiolibro (la caché se conserva).");
        Console.WriteLine("  --translate-from <lang> Traduce el libro con Ollama desde este idioma (p. ej. en).");
        Console.WriteLine($"  --translate-to <lang>   Idioma destino de la traducción (por defecto: {DefaultTranslateTo}).");
        Console.WriteLine($"  --ollama-url <url>      Endpoint de Ollama (por defecto: {DefaultOllamaUrl}).");
        Console.WriteLine($"  --ollama-model <modelo> Modelo de Ollama (por defecto: {DefaultOllamaModel}).");
    }
}
