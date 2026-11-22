using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class BookConversionPipelineTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"conversor_pipe_{Guid.NewGuid():N}");

    [Fact]
    public async Task ConvertAsync_UsesCacheOnSecondRun_AndAssemblesChapters()
    {
        var fakeBook = new Book(
            "Libro Test",
            "Autor",
            "es-ES",
            [new Chapter(1, "Capítulo I", "Hola Sr. Pérez. Todo al 100%.")]);

        var tts = new CountingFakeTtsEngine();
        var cache = new FileSystemChunkCache(Path.Combine(_tempDir, "cache"));
        var pipeline = new BookConversionPipeline(
            new FakeBookSource(fakeBook),
            new SpanishTextNormalizer(),
            new SentenceChunker(),
            tts,
            cache,
            new WavChapterAssembler());

        var outDir = Path.Combine(_tempDir, "out");
        var first = await pipeline.ConvertAsync("dummy.epub", outDir, "ef_dora");
        var callsAfterFirst = tts.CallCount;

        var second = await pipeline.ConvertAsync("dummy.epub", outDir, "ef_dora");

        Assert.Single(first);
        Assert.Single(second);
        Assert.True(callsAfterFirst > 0);
        Assert.Equal(callsAfterFirst, tts.CallCount);
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

    private sealed class CountingFakeTtsEngine : ITtsEngine
    {
        public int CallCount { get; private set; }
        public string ModelVersion => "fake-v1";

        public Task<byte[]> SynthesizeWavAsync(string text, string voiceId, float speed, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var wav = new byte[44 + 16];
            "RIFF"u8.CopyTo(wav.AsSpan(0, 4));
            "WAVE"u8.CopyTo(wav.AsSpan(8, 4));
            return Task.FromResult(wav);
        }
    }
}
