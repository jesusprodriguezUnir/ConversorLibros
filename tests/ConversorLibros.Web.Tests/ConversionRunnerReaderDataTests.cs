using System.Text.Json;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Web.Services;
using Xunit;

namespace ConversorLibros.Web.Tests;

/// <summary>Reader-data step the runner executes after a successful export (<see cref="ReaderDataPublisher"/>).</summary>
public sealed class ConversionRunnerReaderDataTests : IDisposable
{
    private readonly string _bookDir = Path.Combine(Path.GetTempPath(), "ConversorLibros.Web.Tests", Guid.NewGuid().ToString("N"));
    private string ReaderDir => Path.Combine(_bookDir, "reader");

    public ConversionRunnerReaderDataTests() => Directory.CreateDirectory(_bookDir);

    [Fact]
    public async Task PublishAsync_Opus_WritesSidecarsAndReturnsNoWarning()
    {
        var warning = await ReaderDataPublisher.PublishAsync(
            [Map(1, 1000), Map(2, 2000)], expectedChapters: 2, "Libro", _bookDir, AudiobookFormat.Opus, CancellationToken.None);

        Assert.Null(warning);
        Assert.True(File.Exists(Path.Combine(ReaderDir, "chapter_001.json")));
        Assert.True(File.Exists(Path.Combine(ReaderDir, "chapter_002.json")));
        Assert.True(File.Exists(Path.Combine(ReaderDir, "index.json")));
    }

    [Fact]
    public async Task PublishAsync_Opus_WritesAlignedParagraphs()
    {
        await ReaderDataPublisher.PublishAsync(
            [Map(1, 1000)], expectedChapters: 1, "Libro", _bookDir, AudiobookFormat.Opus, CancellationToken.None);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(ReaderDir, "chapter_001.json")));
        var paragraph = Assert.Single(json.RootElement.GetProperty("paragraphs").EnumerateArray());
        Assert.Equal("sentence", paragraph.GetProperty("mode").GetString());
        var sentence = Assert.Single(paragraph.GetProperty("sentences").EnumerateArray());
        Assert.Equal("Texto.", sentence.GetProperty("text").GetString());
        Assert.Equal(1000, sentence.GetProperty("endMs").GetInt64());
    }

    [Fact]
    public async Task PublishAsync_M4b_WritesNothing()
    {
        var warning = await ReaderDataPublisher.PublishAsync(
            [Map(1, 1000)], expectedChapters: 1, "Libro", _bookDir, AudiobookFormat.M4b, CancellationToken.None);

        Assert.Null(warning);
        Assert.False(Directory.Exists(ReaderDir));
    }

    [Fact]
    public async Task PublishAsync_MissingMapForAChapter_WritesTheRestAndWarns()
    {
        // The pipeline skips the callback when SyncMapBuilder returns null (byteRate mismatch), so fewer maps than chapters arrive.
        var warning = await ReaderDataPublisher.PublishAsync(
            [Map(1, 1000)], expectedChapters: 2, "Libro", _bookDir, AudiobookFormat.Opus, CancellationToken.None);

        Assert.Equal(ReaderDataPublisher.Warning, warning);
        Assert.True(File.Exists(Path.Combine(ReaderDir, "chapter_001.json")));
        Assert.False(File.Exists(Path.Combine(ReaderDir, "chapter_002.json")));
    }

    [Fact]
    public async Task PublishAsync_WriterThrows_ReturnsWarningInsteadOfFailing()
    {
        // A file squatting on the reader folder path makes the writer throw.
        await File.WriteAllTextAsync(ReaderDir, "not a directory");

        var warning = await ReaderDataPublisher.PublishAsync(
            [Map(1, 1000)], expectedChapters: 1, "Libro", _bookDir, AudiobookFormat.Opus, CancellationToken.None);

        Assert.Equal(ReaderDataPublisher.Warning, warning);
        Assert.Equal("Audiolibro creado, pero no se pudieron generar los datos de lectura.", warning);
    }

    [Fact]
    public async Task PublishAsync_Cancelled_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReaderDataPublisher.PublishAsync(
            [Map(1, 1000)], expectedChapters: 1, "Libro", _bookDir, AudiobookFormat.Opus, cts.Token));
    }

    [Fact]
    public async Task ExportThenPublishAsync_ExportSucceeds_WritesSidecarsAfterExport()
    {
        var exporter = new FakeExporter(fail: false, readerDirToObserve: ReaderDir);

        var result = await ReaderDataPublisher.ExportThenPublishAsync(
            exporter, TestBook, [], _bookDir, [Map(0, 1000)], expectedChapters: 1, _bookDir, AudiobookFormat.Opus, CancellationToken.None);

        Assert.Equal(FakeExporter.OutputPath, result.OutputPath);
        Assert.Null(result.Warning);
        Assert.False(exporter.ReaderDirExistedDuringExport);
        Assert.True(File.Exists(Path.Combine(ReaderDir, "index.json")));
    }

    [Fact]
    public async Task ExportThenPublishAsync_ExportFails_PropagatesAndWritesNoSidecars()
    {
        var exporter = new FakeExporter(fail: true, readerDirToObserve: ReaderDir);

        await Assert.ThrowsAsync<IOException>(() => ReaderDataPublisher.ExportThenPublishAsync(
            exporter, TestBook, [], _bookDir, [Map(0, 1000)], expectedChapters: 1, _bookDir, AudiobookFormat.Opus, CancellationToken.None));

        Assert.False(Directory.Exists(ReaderDir));
    }

    private static readonly Book TestBook = new("Libro", "Autor", "es", []);

    private sealed class FakeExporter(bool fail, string readerDirToObserve) : IAudiobookExporter
    {
        public const string OutputPath = "exported";

        public bool ReaderDirExistedDuringExport { get; private set; }

        public AudiobookFormat Format => AudiobookFormat.Opus;

        public Task<string> ExportAsync(Book book, IReadOnlyList<AudiobookChapter> chapters, string outputRoot, CancellationToken cancellationToken = default)
        {
            ReaderDirExistedDuringExport = Directory.Exists(readerDirToObserve);
            return fail ? Task.FromException<string>(new IOException("ffmpeg failed")) : Task.FromResult(OutputPath);
        }
    }

    private static ChapterSyncMap Map(int index, long durationMs) =>
        new(index, $"Cap {index}", "Texto.", durationMs, [new ChunkTiming(0, "Texto.", 0, durationMs)]);

    public void Dispose()
    {
        if (Directory.Exists(_bookDir))
        {
            Directory.Delete(_bookDir, recursive: true);
        }
    }
}
