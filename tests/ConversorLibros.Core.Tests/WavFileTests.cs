using System.Buffers.Binary;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class WavFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"conversor_wav_{Guid.NewGuid():N}");

    public WavFileTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void ReadPcmInfo_ValidMonoPcm16_ReportsHeaderFields()
    {
        var path = WriteWav("ok.wav", dataBytes: 48000, sampleRate: 24000);

        var info = WavFile.ReadPcmInfo(path);

        Assert.Equal(24000, info.SampleRate);
        Assert.Equal(1, info.Channels);
        Assert.Equal(16, info.BitsPerSample);
        Assert.Equal(48000, info.ByteRate);
        Assert.Equal(44, info.DataOffset);
        Assert.Equal(48000, info.DataBytes);
        Assert.Equal(1000, info.DataBytes * 1000 / info.ByteRate);
    }

    [Fact]
    public void ReadPcmInfo_ExtraRiffChunksBeforeData_FindsDataOffset()
    {
        var path = WriteWav("extra.wav", dataBytes: 9600, sampleRate: 24000, listChunkBytes: 10);

        var info = WavFile.ReadPcmInfo(path);

        Assert.Equal(44 + 8 + 10, info.DataOffset);
        Assert.Equal(9600, info.DataBytes);
        Assert.Equal(48000, info.ByteRate);
    }

    [Fact]
    public void ReadPcmInfo_DataSizeLargerThanFile_IsClampedToFileLength()
    {
        var path = WriteWav("streamed.wav", dataBytes: 100, sampleRate: 24000, declaredDataBytes: uint.MaxValue);

        Assert.Equal(100, WavFile.ReadPcmInfo(path).DataBytes);
    }

    [Fact]
    public void ReadPcmInfo_FileShorterThanHeader_ThrowsInvalidData()
    {
        var path = Path.Combine(_dir, "short.wav");
        File.WriteAllBytes(path, "RIFF"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => WavFile.ReadPcmInfo(path));
    }

    [Fact]
    public void ReadPcmInfo_NonPcmFormat_ThrowsInvalidData()
    {
        var path = WriteWav("float.wav", dataBytes: 400, sampleRate: 24000, audioFormat: 3);

        Assert.Throws<InvalidDataException>(() => WavFile.ReadPcmInfo(path));
    }

    [Fact]
    public void ReadPcmInfo_NotRiff_ThrowsInvalidData()
    {
        var path = Path.Combine(_dir, "junk.wav");
        File.WriteAllBytes(path, new byte[64]);

        Assert.Throws<InvalidDataException>(() => WavFile.ReadPcmInfo(path));
    }

    private string WriteWav(
        string name,
        int dataBytes,
        int sampleRate,
        int listChunkBytes = 0,
        ushort audioFormat = 1,
        uint? declaredDataBytes = null)
    {
        const short channels = 1;
        const short bits = 16;
        var byteRate = sampleRate * channels * bits / 8;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write((uint)0);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write((uint)16);
        w.Write(audioFormat);
        w.Write(channels);
        w.Write((uint)sampleRate);
        w.Write((uint)byteRate);
        w.Write((short)(channels * bits / 8));
        w.Write(bits);
        if (listChunkBytes > 0)
        {
            w.Write("LIST"u8);
            w.Write((uint)listChunkBytes);
            w.Write(new byte[listChunkBytes]);
        }

        w.Write("data"u8);
        w.Write(declaredDataBytes ?? (uint)dataBytes);
        w.Write(new byte[dataBytes]);
        w.Flush();

        var bytes = ms.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        var path = Path.Combine(_dir, name);
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
