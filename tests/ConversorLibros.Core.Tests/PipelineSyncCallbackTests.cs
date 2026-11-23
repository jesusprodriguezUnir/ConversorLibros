using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

/// <summary>Covers the optional <c>onChapterSynced</c> callback of <see cref="BookConversionPipeline.ConvertAsync"/>.</summary>
public class PipelineSyncCallbackTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"conversor_sync_pipe_{Guid.NewGuid():N}");

    [Fact]
    public async Task ConvertAsync_InvokesCallbackOncePerChapter_WithTimedChunks()
    {
        var book = new Book("Libro", "Autor", "es", [
            new Chapter(1, "Uno", "Hola mundo."),
            new Chapter(2, "Dos", "Adiós mundo.")]);
        var maps = new List<ChapterSyncMap>();

        var wavs = await CreatePipeline(book, new WavTtsEngine(dataBytes: 4800))
            .ConvertAsync("d.epub", Path.Combine(_tempDir, "out"), "0", onChapterSynced: maps.Add);

        Assert.Equal(2, wavs.Count);
        Assert.Equal([1, 2], maps.Select(m => m.ChapterIndex));
        Assert.Equal(["Hola mundo.", "Adiós mundo."], maps.Select(m => m.RawText));
        Assert.All(maps, m => Assert.Equal(100, m.DurationMs));
        Assert.All(maps, m => Assert.NotEmpty(m.Chunks));
    }

    [Fact]
    public async Task ConvertAsync_SecondRunFromCache_ProducesIdenticalMapWithoutSynthesis()
    {
        var book = new Book("Libro", "Autor", "es", [new Chapter(1, "Uno", "Hola mundo. Otra frase.")]);
        var tts = new WavTtsEngine(dataBytes: 4800);
        var pipeline = CreatePipeline(book, tts);
        var first = new List<ChapterSyncMap>();
        var second = new List<ChapterSyncMap>();

        await pipeline.ConvertAsync("d.epub", Path.Combine(_tempDir, "out"), "0", onChapterSynced: first.Add);
        var calls = tts.CallCount;
        await pipeline.ConvertAsync("d.epub", Path.Combine(_tempDir, "out"), "0", onChapterSynced: second.Add);

        Assert.True(calls > 0);
        Assert.Equal(calls, tts.CallCount);
        Assert.Equal(first.Single(), second.Single(), MapComparer.Instance);
    }

    [Fact]
    public async Task ConvertAsync_Translated_CallbackCarriesSpokenText()
    {
        var book = new Book("Book", "Author", "en", [new Chapter(1, "Chapter One", "Hello there.")]);
        var maps = new List<ChapterSyncMap>();
        var translator = new CachedChapterTranslator(
            new PrefixTranslator(),
            new FileSystemTranslationCache(Path.Combine(_tempDir, "tcache")));

        await CreatePipeline(book, new WavTtsEngine(4800), translator)
            .ConvertAsync("d.epub", Path.Combine(_tempDir, "out"), "0", sourceLanguage: "en", targetLanguage: "es", onChapterSynced: maps.Add);

        var map = Assert.Single(maps);
        Assert.Equal("ES(Hello there.)", map.RawText);
        Assert.Equal("ES(Chapter One)", map.Title);
    }

    [Fact]
    public async Task ConvertAsync_CallbackOmitted_StillConverts()
    {
        var book = new Book("Libro", "Autor", "es", [new Chapter(1, "Uno", "Hola mundo.")]);

        var wavs = await CreatePipeline(book, new WavTtsEngine(4800))
            .ConvertAsync("d.epub", Path.Combine(_tempDir, "out"), "0");

        Assert.True(File.Exists(Assert.Single(wavs)));
    }

    [Fact]
    public async Task ConvertAsync_BuilderReturnsNull_SkipsCallbackButKeepsAudio()
    {
        // Header-less fake WAV: SyncMapBuilder cannot read it and returns null.
        var book = new Book("Libro", "Autor", "es", [new Chapter(1, "Uno", "Hola mundo.")]);
        var maps = new List<ChapterSyncMap>();

        var wavs = await CreatePipeline(book, new WavTtsEngine(4800, validHeader: false))
            .ConvertAsync("d.epub", Path.Combine(_tempDir, "out"), "0", onChapterSynced: maps.Add);

        Assert.Single(wavs);
        Assert.Empty(maps);
    }

    private BookConversionPipeline CreatePipeline(Book book, ITtsEngine tts, CachedChapterTranslator? translator = null) =>
        new(
            new FakeBookSource(book),
            new SpanishTextNormalizer(),
            new SentenceChunker(),
            tts,
            new FileSystemChunkCache(Path.Combine(_tempDir, "cache")),
            new WavChapterAssembler(),
            translator);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private sealed class MapComparer : IEqualityComparer<ChapterSyncMap>
    {
        public static readonly MapComparer Instance = new();

        public bool Equals(ChapterSyncMap? x, ChapterSyncMap? y) =>
            x is not null && y is not null
            && x.ChapterIndex == y.ChapterIndex && x.Title == y.Title && x.RawText == y.RawText
            && x.DurationMs == y.DurationMs && x.Chunks.SequenceEqual(y.Chunks);

        public int GetHashCode(ChapterSyncMap obj) => obj.ChapterIndex;
    }

    private sealed class FakeBookSource(Book book) : IBookSource
    {
        public Task<Book> LoadAsync(string filePath, CancellationToken cancellationToken = default) => Task.FromResult(book);
    }

    private sealed class PrefixTranslator : ITranslator
    {
        public string ModelVersion => "fake-translator|v1";

        public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default) =>
            Task.FromResult($"ES({text})");
    }

    /// <summary>Emits mono PCM16 24 kHz WAVs (48000 bytes/s), so 4800 data bytes last 100 ms.</summary>
    private sealed class WavTtsEngine(int dataBytes, bool validHeader = true) : ITtsEngine
    {
        public int CallCount { get; private set; }
        public string ModelVersion => "fake-wav-v1";

        public Task<byte[]> SynthesizeWavAsync(string text, string voiceId, float speed, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var wav = new byte[44 + dataBytes];
            "RIFF"u8.CopyTo(wav.AsSpan(0, 4));
            "WAVE"u8.CopyTo(wav.AsSpan(8, 4));
            if (validHeader)
            {
                "fmt "u8.CopyTo(wav.AsSpan(12, 4));
                BitConverter.TryWriteBytes(wav.AsSpan(16, 4), 16);
                BitConverter.TryWriteBytes(wav.AsSpan(20, 2), (short)1);
                BitConverter.TryWriteBytes(wav.AsSpan(22, 2), (short)1);
                BitConverter.TryWriteBytes(wav.AsSpan(24, 4), 24000);
                BitConverter.TryWriteBytes(wav.AsSpan(28, 4), 48000);
                BitConverter.TryWriteBytes(wav.AsSpan(32, 2), (short)2);
                BitConverter.TryWriteBytes(wav.AsSpan(34, 2), (short)16);
                "data"u8.CopyTo(wav.AsSpan(36, 4));
                BitConverter.TryWriteBytes(wav.AsSpan(40, 4), dataBytes);
            }

            return Task.FromResult(wav);
        }
    }
}
