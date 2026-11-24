using System.Buffers.Binary;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class WavChapterAssemblerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"conversor_wav_{Guid.NewGuid():N}");

    [Fact]
    public async Task AssembleChapterAsync_CombinesChunksAndUpdatesWavHeaderSizes()
    {
        Directory.CreateDirectory(_tempDir);
        var chunk1 = Path.Combine(_tempDir, "c1.wav");
        var chunk2 = Path.Combine(_tempDir, "c2.wav");

        await File.WriteAllBytesAsync(chunk1, CreateSyntheticWav(100));
        await File.WriteAllBytesAsync(chunk2, CreateSyntheticWav(150));

        var assembler = new WavChapterAssembler();
        var outDir = Path.Combine(_tempDir, "out");
        var resultPath = await assembler.AssembleChapterAsync(1, [chunk1, chunk2], outDir);

        Assert.True(File.Exists(resultPath));
        var resultBytes = await File.ReadAllBytesAsync(resultPath);
        Assert.Equal(44 + 250, resultBytes.Length);

        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(resultBytes.AsSpan(4, 4));
        var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(resultBytes.AsSpan(40, 4));
        Assert.Equal((uint)(36 + 250), riffSize);
        Assert.Equal((uint)250, dataSize);
    }

    [Fact]
    public async Task AssembleChapterAsync_InsertsSilenceBytesMatchingPausesAfter()
    {
        Directory.CreateDirectory(_tempDir);
        var chunk1 = Path.Combine(_tempDir, "c1.wav");
        var chunk2 = Path.Combine(_tempDir, "c2.wav");

        await File.WriteAllBytesAsync(chunk1, CreateSyntheticWav(100, sampleRate: 22050, fillByte: 0x2A));
        await File.WriteAllBytesAsync(chunk2, CreateSyntheticWav(150, sampleRate: 22050, fillByte: 0x3B));

        var assembler = new WavChapterAssembler();
        var outDir = Path.Combine(_tempDir, "out");
        var pauses = new[] { TimeSpan.FromMilliseconds(500), TimeSpan.Zero };

        var resultPath = await assembler.AssembleChapterAsync(1, [chunk1, chunk2], outDir, pauses);

        Assert.True(File.Exists(resultPath));
        var resultBytes = await File.ReadAllBytesAsync(resultPath);
        const int expectedSilenceBytes = 22050;
        const int expectedDataSize = 100 + 150 + expectedSilenceBytes;

        Assert.Equal(44 + expectedDataSize, resultBytes.Length);

        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(resultBytes.AsSpan(4, 4));
        var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(resultBytes.AsSpan(40, 4));
        Assert.Equal((uint)(36 + expectedDataSize), riffSize);
        Assert.Equal((uint)expectedDataSize, dataSize);

        Assert.All(resultBytes.AsSpan(44, 100).ToArray(), b => Assert.Equal((byte)0x2A, b));
        Assert.All(resultBytes.AsSpan(44 + 100, expectedSilenceBytes).ToArray(), b => Assert.Equal((byte)0, b));
        Assert.All(resultBytes.AsSpan(44 + 100 + expectedSilenceBytes, 150).ToArray(), b => Assert.Equal((byte)0x3B, b));
    }

    private static byte[] CreateSyntheticWav(int dataBytes, int sampleRate = 24000, byte fillByte = 0)
    {
        var bytes = new byte[44 + dataBytes];
        "RIFF"u8.CopyTo(bytes.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(36 + dataBytes));
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20, 2), 1); // PCM
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22, 2), 1); // Mono
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24, 4), (uint)sampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), (uint)(sampleRate * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34, 2), 16);
        "data"u8.CopyTo(bytes.AsSpan(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40, 4), (uint)dataBytes);
        if (fillByte != 0)
        {
            bytes.AsSpan(44).Fill(fillByte);
        }
        return bytes;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
