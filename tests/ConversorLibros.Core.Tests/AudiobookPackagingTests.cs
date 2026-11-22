using System.Buffers.Binary;
using System.Text;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class AudiobookPackagingTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"conversor_pkg_{Guid.NewGuid():N}");

    public AudiobookPackagingTests() => Directory.CreateDirectory(_tempDir);

    [Theory]
    [InlineData("Capítulo 1: El inicio?", "Capítulo 1 El inicio")]
    [InlineData("a<b>c|d*e", "a b c d e")]
    [InlineData("  trailing dots...  ", "trailing dots")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("", "Audiolibro")]
    [InlineData("...", "Audiolibro")]
    public void SanitizeComponent_ReplacesForbiddenCharactersAndReservedNames(string input, string expected)
    {
        Assert.Equal(expected, AudiobookFileNames.SanitizeComponent(input));
    }

    [Fact]
    public void SanitizeComponent_CapsLengthAndNeverEndsWithDot()
    {
        var longTitle = new string('a', 200) + " ..";
        var result = AudiobookFileNames.SanitizeComponent(longTitle);

        Assert.True(result.Length <= AudiobookFileNames.MaxComponentLength);
        Assert.DoesNotContain(result[^1], new[] { '.', ' ' });
    }

    [Fact]
    public void ChapterFileName_UsesZeroPaddedIndexAndSortsByChapterNumber()
    {
        var names = new[] { 12, 2, 1, 10 }
            .Select(i => AudiobookFileNames.ChapterFileName(i, $"Título {i}"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["001 - Título 1.opus", "002 - Título 2.opus", "010 - Título 10.opus", "012 - Título 12.opus"], names);
    }

    [Fact]
    public void ChapterFileName_FallsBackToNumberedTitleWhenEmpty()
    {
        Assert.Equal("003 - Capítulo 3.opus", AudiobookFileNames.ChapterFileName(3, "  "));
    }

    [Fact]
    public void OpusChapter_PassesMetadataAsSingleArgumentsWithoutShellQuoting()
    {
        const string title = "Él dijo \"hola\": 'adiós'";
        var args = FfmpegCommands.OpusChapter("in.wav", "out.opus.partial", title, "Autor, Ana", "Álbum", 7);

        Assert.Contains("-c:a", args);
        Assert.Equal("libopus", args[Array.IndexOf(args.ToArray(), "-c:a") + 1]);
        Assert.Equal("16k", args[Array.IndexOf(args.ToArray(), "-b:a") + 1]);
        Assert.Equal("voip", args[Array.IndexOf(args.ToArray(), "-application") + 1]);
        Assert.Equal("1", args[Array.IndexOf(args.ToArray(), "-ac") + 1]);
        Assert.Contains($"title={title}", args);
        Assert.Contains("artist=Autor, Ana", args);
        Assert.Contains("album=Álbum", args);
        Assert.Contains("track=7", args);
        Assert.Equal("in.wav", args[Array.IndexOf(args.ToArray(), "-i") + 1]);
        Assert.Equal("out.opus.partial", args[^1]);
    }

    [Fact]
    public void M4bPackage_AttachesCoverOnlyWhenProvided()
    {
        var withCover = FfmpegCommands.M4bPackage("concat.txt", "meta.txt", "cover.jpg", "book.m4b");
        var withoutCover = FfmpegCommands.M4bPackage("concat.txt", "meta.txt", null, "book.m4b");

        Assert.Contains("attached_pic", withCover);
        Assert.DoesNotContain("attached_pic", withoutCover);
        Assert.Equal("book.m4b", withCover[^1]);
        Assert.Equal("book.m4b", withoutCover[^1]);
    }

    [Fact]
    public void FfmetadataBuilder_AccumulatesChapterBoundariesAndEscapesValues()
    {
        var meta = FfmetadataBuilder.Build("Libro=1; #uno", "Ana", [("Cap A", TimeSpan.FromSeconds(1.5)), ("Cap B", TimeSpan.FromSeconds(2))]);

        Assert.StartsWith(";FFMETADATA1\n", meta);
        Assert.Contains("title=Libro\\=1\\; \\#uno", meta);
        Assert.Contains("START=0\nEND=1500\ntitle=Cap A", meta);
        Assert.Contains("START=1500\nEND=3500\ntitle=Cap B", meta);
    }

    [Fact]
    public void WavFile_ReadDuration_DerivesDurationFromByteRateAndDataSize()
    {
        var path = Path.Combine(_tempDir, "one_second.wav");
        File.WriteAllBytes(path, CreateSyntheticWav(48000)); // byte rate is 48000 in the helper

        Assert.Equal(TimeSpan.FromSeconds(1), WavFile.ReadDuration(path));
    }

    [Fact]
    public void OggOpusInfo_ReadsDurationFromLastPageAndTagsFromHeader()
    {
        var tags = OpusTagsPacket(("ARTIST", "Ana García"), ("ALBUM", "Mi libro"));
        var bytes = Concat(
            OggPage(0, tags),
            OggPage(48000L * 90, new byte[] { 1, 2, 3, 4 }));
        var path = Path.Combine(_tempDir, "chapter.opus");
        File.WriteAllBytes(path, bytes);

        var info = OggOpusInfo.Read(path);

        Assert.Equal(TimeSpan.FromSeconds(90), info.Duration);
        Assert.Equal("Ana García", info.Tags["ARTIST"]);
        Assert.Equal("Mi libro", info.Tags["ALBUM"]);
    }

    [Fact]
    public void OggOpusInfo_IgnoresFalseCapturePatternInsideAudioPayload()
    {
        var fakeInsideAudio = Encoding.ASCII.GetBytes("xxOggS\u0001\u0002garbage");
        var bytes = Concat(
            OggPage(48000L * 5, OpusTagsPacket()),
            OggPage(48000L * 30, fakeInsideAudio));
        var path = Path.Combine(_tempDir, "tricky.opus");
        File.WriteAllBytes(path, bytes);

        Assert.Equal(TimeSpan.FromSeconds(30), OggOpusInfo.Read(path).Duration);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    internal static byte[] CreateSyntheticWav(int dataBytes)
    {
        var bytes = new byte[44 + dataBytes];
        "RIFF"u8.CopyTo(bytes.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(36 + dataBytes));
        "WAVEfmt "u8.CopyTo(bytes.AsSpan(8, 8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24, 4), 24000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), 48000);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34, 2), 16);
        "data"u8.CopyTo(bytes.AsSpan(36, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40, 4), (uint)dataBytes);
        return bytes;
    }

    internal static byte[] OggPage(long granule, byte[] payload)
    {
        var segments = new List<byte>();
        var remaining = payload.Length;
        while (remaining >= 255)
        {
            segments.Add(255);
            remaining -= 255;
        }

        segments.Add((byte)remaining);

        var header = new byte[27 + segments.Count];
        "OggS"u8.CopyTo(header);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(6, 8), granule);
        header[26] = (byte)segments.Count;
        segments.CopyTo(header, 27);
        return Concat(header, payload);
    }

    internal static byte[] OpusTagsPacket(params (string Key, string Value)[] comments)
    {
        var ms = new MemoryStream();
        ms.Write("OpusTags"u8);
        WriteUInt32(ms, 6);
        ms.Write("vendor"u8);
        WriteUInt32(ms, (uint)comments.Length);
        foreach (var (key, value) in comments)
        {
            var bytes = Encoding.UTF8.GetBytes($"{key}={value}");
            WriteUInt32(ms, (uint)bytes.Length);
            ms.Write(bytes);
        }

        return ms.ToArray();
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
}
