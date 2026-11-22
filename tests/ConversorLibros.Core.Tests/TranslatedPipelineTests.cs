using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class TranslatedPipelineTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"conversor_tpipe_{Guid.NewGuid():N}");

    [Fact]
    public async Task ConvertAsync_WithTranslation_SynthesizesTranslatedText()
    {
        var book = new Book("Book", "Author", "en", [new Chapter(1, "Chapter One", "Hello there.")]);
        var tts = new RecordingTtsEngine();
        var sut = CreatePipeline(book, tts, new PrefixTranslator());

        await sut.ConvertAsync("dummy.epub", Path.Combine(_tempDir, "out"), "0", sourceLanguage: "en", targetLanguage: "es");

        Assert.NotEmpty(tts.Texts);
        Assert.All(tts.Texts, text => Assert.Contains("ES(", text));
    }

    [Fact]
    public async Task ConvertAsync_WithTranslation_ReportsTranslatingStage()
    {
        var book = new Book("Book", "Author", "en", [new Chapter(1, "Chapter One", "Hello there.")]);
        var progress = new SynchronousProgress();
        var sut = CreatePipeline(book, new RecordingTtsEngine(), new PrefixTranslator());

        await sut.ConvertAsync("dummy.epub", Path.Combine(_tempDir, "out"), "0", progress: progress, sourceLanguage: "en", targetLanguage: "es");

        Assert.Contains("Translating", progress.Stages);
    }

    [Fact]
    public async Task ConvertAsync_LanguagesWithoutTranslator_Throws()
    {
        var book = new Book("Book", "Author", "en", [new Chapter(1, "Chapter One", "Hello there.")]);
        var sut = new BookConversionPipeline(
            new FakeBookSource(book),
            new SpanishTextNormalizer(),
            new SentenceChunker(),
            new RecordingTtsEngine(),
            new FileSystemChunkCache(Path.Combine(_tempDir, "cache")),
            new WavChapterAssembler());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.ConvertAsync("dummy.epub", Path.Combine(_tempDir, "out"), "0", sourceLanguage: "en", targetLanguage: "es"));
    }

    [Fact]
    public async Task ConvertAsync_OnlyOneLanguageProvided_Throws()
    {
        var book = new Book("Book", "Author", "en", [new Chapter(1, "Chapter One", "Hello there.")]);
        var sut = CreatePipeline(book, new RecordingTtsEngine(), new PrefixTranslator());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ConvertAsync("dummy.epub", Path.Combine(_tempDir, "out"), "0", sourceLanguage: "en"));
    }

    [Fact]
    public async Task ConvertAsync_WithoutLanguages_DoesNotTranslate()
    {
        var book = new Book("Book", "Author", "es", [new Chapter(1, "Capítulo I", "Hola mundo.")]);
        var tts = new RecordingTtsEngine();
        var translator = new PrefixTranslator();
        var sut = CreatePipeline(book, tts, translator);

        await sut.ConvertAsync("dummy.epub", Path.Combine(_tempDir, "out"), "0");

        Assert.Empty(translator.Calls);
        Assert.Contains(tts.Texts, text => text.Contains("Hola mundo"));
    }

    private BookConversionPipeline CreatePipeline(Book book, ITtsEngine tts, ITranslator translator)
    {
        var cachedTranslator = new CachedChapterTranslator(translator, new FileSystemTranslationCache(Path.Combine(_tempDir, "tcache")));
        return new BookConversionPipeline(
            new FakeBookSource(book),
            new SpanishTextNormalizer(),
            new SentenceChunker(),
            tts,
            new FileSystemChunkCache(Path.Combine(_tempDir, "cache")),
            new WavChapterAssembler(),
            cachedTranslator);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class FakeBookSource(Book book) : IBookSource
    {
        public Task<Book> LoadAsync(string filePath, CancellationToken cancellationToken = default) => Task.FromResult(book);
    }

    private sealed class PrefixTranslator : ITranslator
    {
        public List<string> Calls { get; } = [];
        public string ModelVersion => "fake-translator|v1";

        public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
        {
            Calls.Add(text);
            return Task.FromResult($"ES({text})");
        }
    }

    private sealed class SynchronousProgress : IProgress<ConversionProgress>
    {
        public List<string> Stages { get; } = [];

        public void Report(ConversionProgress value) => Stages.Add(value.Stage);
    }

    private sealed class RecordingTtsEngine : ITtsEngine
    {
        public List<string> Texts { get; } = [];
        public string ModelVersion => "fake-tts-v1";

        public Task<byte[]> SynthesizeWavAsync(string text, string voiceId, float speed, CancellationToken cancellationToken = default)
        {
            Texts.Add(text);
            var wav = new byte[44 + 16];
            "RIFF"u8.CopyTo(wav.AsSpan(0, 4));
            "WAVE"u8.CopyTo(wav.AsSpan(8, 4));
            return Task.FromResult(wav);
        }
    }
}
