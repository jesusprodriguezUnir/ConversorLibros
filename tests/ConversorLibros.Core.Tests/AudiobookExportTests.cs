using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class AudiobookExportTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"conversor_export_{Guid.NewGuid():N}");

    public AudiobookExportTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public void FfmpegLocator_FindsExecutableOnPathBeforeWinGet()
    {
        var pathDir = Directory.CreateDirectory(Path.Combine(_tempDir, "bin-on-path")).FullName;
        var localAppData = Path.Combine(_tempDir, "local");
        CreateFakeFfmpeg(Path.Combine(localAppData, "Microsoft", "WinGet", "Links"));
        File.WriteAllText(Path.Combine(pathDir, "ffmpeg.exe"), "");

        var found = FfmpegLocator.Locate($"{Path.Combine(_tempDir, "missing")}{Path.PathSeparator}{pathDir}", localAppData);

        Assert.Equal(Path.Combine(pathDir, "ffmpeg.exe"), found);
    }

    [Fact]
    public void FfmpegLocator_PicksLatestWinGetPackageDeterministically()
    {
        var packages = Path.Combine(_tempDir, "local", "Microsoft", "WinGet", "Packages");
        CreateFakeFfmpeg(Path.Combine(packages, "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe", "ffmpeg-7.1-full_build"));
        CreateFakeFfmpeg(Path.Combine(packages, "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe", "ffmpeg-8.1-full_build"));
        CreateFakeFfmpeg(Path.Combine(packages, "OtherVendor.Tool", "tool-1.0"));

        var found = FfmpegLocator.Locate(string.Empty, Path.Combine(_tempDir, "local"));

        Assert.Equal(
            Path.Combine(packages, "Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe", "ffmpeg-8.1-full_build", "bin", "ffmpeg.exe"),
            found);
    }

    [Fact]
    public void FfmpegLocator_FallsBackToWinGetLinks()
    {
        var localAppData = Path.Combine(_tempDir, "local");
        var links = Path.Combine(localAppData, "Microsoft", "WinGet", "Links");
        Directory.CreateDirectory(links);
        File.WriteAllText(Path.Combine(links, "ffmpeg.exe"), "");

        Assert.Equal(Path.Combine(links, "ffmpeg.exe"), FfmpegLocator.Locate(string.Empty, localAppData));
    }

    [Fact]
    public void FfmpegLocator_ReturnsNullWhenNothingIsInstalled()
    {
        Assert.Null(FfmpegLocator.Locate(string.Empty, Path.Combine(_tempDir, "empty")));
    }

    [Fact]
    public async Task AudiobookChapterResolver_UsesChapterTitlesAndFallsBackForUnknownFiles()
    {
        var book = SampleBook();
        var paths = new[] { Path.Combine(_tempDir, "chapter_002.wav"), Path.Combine(_tempDir, "chapter_099.wav") };

        var chapters = await AudiobookChapterResolver.ResolveAsync(book, paths);

        Assert.Equal(new AudiobookChapter(2, "Segundo", paths[0]), chapters[0]);
        Assert.Equal(new AudiobookChapter(0, "chapter_099", paths[1]), chapters[1]);
    }

    [Fact]
    public async Task AudiobookChapterResolver_TranslatesTitlesThroughTheCache()
    {
        var translator = new CachedChapterTranslator(new UppercaseTranslator(), new MemoryTranslationCache());
        var paths = new[] { Path.Combine(_tempDir, "chapter_001.wav") };

        var chapters = await AudiobookChapterResolver.ResolveAsync(SampleBook(), paths, translator, "en", "es");

        Assert.Equal("PRIMERO", chapters[0].Title);
    }

    [Fact]
    public void AudiobookLibrary_ListsOpusFoldersAndM4bFilesAndIgnoresWorkFolders()
    {
        var root = Path.Combine(_tempDir, "salida");
        var bookDir = Path.Combine(root, "Mi libro");
        Directory.CreateDirectory(bookDir);
        File.WriteAllBytes(Path.Combine(bookDir, "001 - Uno.opus"), OggOpusTestData(48000L * 60, "Ana", "Mi libro"));
        File.WriteAllBytes(Path.Combine(bookDir, "002 - Dos.opus"), OggOpusTestData(48000L * 30, "Ana", "Mi libro"));
        File.WriteAllBytes(Path.Combine(bookDir, "cover.jpg"), [0xFF, 0xD8]);

        var work = Path.Combine(root, "cache");
        Directory.CreateDirectory(work);
        File.WriteAllBytes(Path.Combine(work, "abc.wav"), new byte[10]);

        File.WriteAllBytes(Path.Combine(root, "Otro.m4b"), [0]);

        var books = AudiobookLibrary.Scan(root);

        Assert.Equal(2, books.Count);
        var opus = Assert.Single(books, b => b.Format == AudiobookFormat.Opus);
        Assert.Equal("Mi libro", opus.Title);
        Assert.Equal("Ana", opus.Author);
        Assert.Equal(2, opus.ChapterCount);
        Assert.Equal(TimeSpan.FromSeconds(90), opus.Duration);
        Assert.EndsWith("cover.jpg", opus.CoverPath);
        var m4b = Assert.Single(books, b => b.Format == AudiobookFormat.M4b);
        Assert.Equal("Otro", m4b.Title);
    }

    [Fact]
    public async Task OpusFolderExporter_WritesTaggedChaptersAndCover()
    {
        var ffmpeg = CreateFfmpegOrSkip();
        if (ffmpeg is null)
        {
            return; // ffmpeg is optional on the build machine; the CLI e2e run covers this path.
        }

        var wav = Path.Combine(_tempDir, "chapter_001.wav");
        File.WriteAllBytes(wav, AudiobookPackagingTests.CreateSyntheticWav(48000)); // one second of silence
        var book = SampleBook() with { CoverImage = [0x89, 0x50, 0x4E, 0x47], CoverMimeType = "image/png" };
        var chapters = new[] { new AudiobookChapter(1, "Primero: el inicio", wav) };

        var folder = await new OpusFolderExporter(ffmpeg).ExportAsync(book, chapters, Path.Combine(_tempDir, "out"));
        await new OpusFolderExporter(ffmpeg).ExportAsync(book, chapters, Path.Combine(_tempDir, "out")); // re-export replaces

        Assert.Equal(Path.Combine(_tempDir, "out", "Libro de prueba"), folder);
        var files = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(f => f).ToArray();
        Assert.Equal(["001 - Primero el inicio.opus", "cover.png"], files);

        var info = OggOpusInfo.Read(Path.Combine(folder, "001 - Primero el inicio.opus"));
        Assert.Equal("Primero: el inicio", info.Tags["TITLE"]);
        Assert.Equal("Autor de prueba", info.Tags["ARTIST"]);
        Assert.Equal("Libro de prueba", info.Tags["ALBUM"]);
        Assert.True(info.Duration is { } d && Math.Abs(d.TotalSeconds - 1) < 0.1, $"Unexpected duration {info.Duration}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static Book SampleBook() => new(
        "Libro de prueba",
        "Autor de prueba",
        "en",
        [new Chapter(1, "Primero", "Uno."), new Chapter(2, "Segundo", "Dos.")]);

    private static string? CreateFfmpegOrSkip() => FfmpegLocator.Locate();

    private static void CreateFakeFfmpeg(string versionDir)
    {
        var bin = Path.Combine(versionDir, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "ffmpeg.exe"), "");
    }

    private static byte[] OggOpusTestData(long granule, string artist, string album)
    {
        var tags = AudiobookPackagingTests.OpusTagsPacket(("ARTIST", artist), ("ALBUM", album));
        return [.. AudiobookPackagingTests.OggPage(0, tags), .. AudiobookPackagingTests.OggPage(granule, [1, 2, 3])];
    }

    private sealed class UppercaseTranslator : ITranslator
    {
        public string ModelVersion => "test";

        public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default) =>
            Task.FromResult(text.ToUpperInvariant());
    }

    private sealed class MemoryTranslationCache : ITranslationCache
    {
        private readonly Dictionary<string, string> _entries = new();

        public Task<string?> TryGetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entries.TryGetValue(key, out var value) ? value : null);

        public Task SaveAsync(string key, string translatedText, CancellationToken cancellationToken = default)
        {
            _entries[key] = translatedText;
            return Task.CompletedTask;
        }
    }
}
