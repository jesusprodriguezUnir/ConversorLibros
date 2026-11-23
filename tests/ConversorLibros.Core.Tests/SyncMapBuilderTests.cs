using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class SyncMapBuilderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"conversor_sync_{Guid.NewGuid():N}");
    private static readonly Chapter Chapter = new(7, "Capítulo VII", "Raw text. More raw text.");

    public SyncMapBuilderTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public async Task Build_SumOfChunkDurations_EqualsAssembledChapterDuration()
    {
        // Odd sizes force fractional milliseconds so per-chunk rounding would drift.
        var paths = new[] { WriteChunk("a", 1001), WriteChunk("b", 2003), WriteChunk("c", 777) };
        var chunks = Chunks("Uno.", "Dos.", "Tres.");
        var wav = await new WavChapterAssembler().AssembleChapterAsync(7, paths, Path.Combine(_dir, "out"));

        var map = SyncMapBuilder.Build(Chapter, chunks, paths);

        Assert.NotNull(map);
        var expectedMs = (long)Math.Round(WavFile.ReadDuration(wav).TotalMilliseconds);
        Assert.Equal(expectedMs, map.DurationMs);
        Assert.Equal(expectedMs, map.Chunks[^1].EndMs);
        Assert.Equal(0, map.Chunks[0].StartMs);
        Assert.Equal(map.Chunks[0].EndMs, map.Chunks[1].StartMs);
        Assert.Equal(map.Chunks[1].EndMs, map.Chunks[2].StartMs);
    }

    [Fact]
    public void Build_CopiesChapterMetadataAndChunkText()
    {
        var paths = new[] { WriteChunk("a", 48000), WriteChunk("b", 48000) };

        var map = SyncMapBuilder.Build(Chapter, Chunks("Hola.", "Adiós."), paths);

        Assert.NotNull(map);
        Assert.Equal(7, map.ChapterIndex);
        Assert.Equal("Capítulo VII", map.Title);
        Assert.Equal("Raw text. More raw text.", map.RawText);
        Assert.Equal(2000, map.DurationMs);
        Assert.Collection(
            map.Chunks,
            c =>
            {
                Assert.Equal(0, c.Order);
                Assert.Equal("Hola.", c.NormalizedText);
                Assert.Equal(0, c.StartMs);
                Assert.Equal(1000, c.EndMs);
            },
            c =>
            {
                Assert.Equal(1, c.Order);
                Assert.Equal("Adiós.", c.NormalizedText);
                Assert.Equal(1000, c.StartMs);
                Assert.Equal(2000, c.EndMs);
            });
    }

    [Fact]
    public void Build_ByteRateMismatch_ReturnsNull()
    {
        var paths = new[] { WriteChunk("a", 4800, sampleRate: 24000), WriteChunk("b", 4800, sampleRate: 22050) };

        Assert.Null(SyncMapBuilder.Build(Chapter, Chunks("Uno.", "Dos."), paths));
    }

    [Fact]
    public void Build_InvalidWav_ReturnsNull()
    {
        var bad = Path.Combine(_dir, "bad.wav");
        File.WriteAllBytes(bad, new byte[10]);

        Assert.Null(SyncMapBuilder.Build(Chapter, Chunks("Uno."), [bad]));
    }

    [Fact]
    public void Build_PathCountDiffersFromChunkCount_Throws()
    {
        var paths = new[] { WriteChunk("a", 480) };

        Assert.Throws<ArgumentException>(() => SyncMapBuilder.Build(Chapter, Chunks("Uno.", "Dos."), paths));
    }

    private static IReadOnlyList<TextChunk> Chunks(params string[] texts) =>
        texts.Select((t, i) => new TextChunk(7, i, t, TimeSpan.Zero, $"hash{i}")).ToList();

    private string WriteChunk(string name, int dataBytes, int sampleRate = 24000)
    {
        var byteRate = sampleRate * 2;
        var bytes = new byte[44 + dataBytes];
        "RIFF"u8.CopyTo(bytes.AsSpan(0, 4));
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), 36 + dataBytes);
        "WAVE"u8.CopyTo(bytes.AsSpan(8, 4));
        "fmt "u8.CopyTo(bytes.AsSpan(12, 4));
        BitConverter.TryWriteBytes(bytes.AsSpan(16, 4), 16);
        BitConverter.TryWriteBytes(bytes.AsSpan(20, 2), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(22, 2), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(24, 4), sampleRate);
        BitConverter.TryWriteBytes(bytes.AsSpan(28, 4), byteRate);
        BitConverter.TryWriteBytes(bytes.AsSpan(32, 2), (short)2);
        BitConverter.TryWriteBytes(bytes.AsSpan(34, 2), (short)16);
        "data"u8.CopyTo(bytes.AsSpan(36, 4));
        BitConverter.TryWriteBytes(bytes.AsSpan(40, 4), dataBytes);
        var path = Path.Combine(_dir, $"{name}.wav");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
