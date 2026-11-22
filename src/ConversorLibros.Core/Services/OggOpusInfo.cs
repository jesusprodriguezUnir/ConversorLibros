using System.Buffers.Binary;
using System.Text;

namespace ConversorLibros.Core.Services;

/// <summary>Duration and Vorbis comment tags of an Ogg Opus file, read from its first and last pages only.</summary>
public sealed record OggOpusInfo(TimeSpan? Duration, IReadOnlyDictionary<string, string> Tags)
{
    /// <summary>Opus granule positions always count 48 kHz samples, regardless of the input rate.</summary>
    private const double GranuleRate = 48000;

    private const int ReadWindow = 64 * 1024;
    private static readonly byte[] PageMagic = "OggS"u8.ToArray();
    private static readonly byte[] TagsMagic = "OpusTags"u8.ToArray();

    public static OggOpusInfo Read(string path)
    {
        using var stream = File.OpenRead(path);
        var length = stream.Length;

        var head = new byte[(int)Math.Min(ReadWindow, length)];
        stream.ReadExactly(head);

        var tail = new byte[(int)Math.Min(ReadWindow, length)];
        stream.Position = length - tail.Length;
        stream.ReadExactly(tail);

        return new OggOpusInfo(ReadDuration(tail), ReadTags(head));
    }

    /// <summary>
    /// Walks back from the end looking for a page header whose payload ends exactly at EOF. That check rejects
    /// "OggS" byte sequences that happen to occur inside audio packets.
    /// </summary>
    private static TimeSpan? ReadDuration(byte[] tail)
    {
        var pos = LastIndexOf(tail, PageMagic, tail.Length);
        while (pos >= 0)
        {
            if (TryGetPageEnd(tail, pos, out var pageEnd) && pageEnd == tail.Length)
            {
                var granule = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(pos + 6, 8));
                return granule > 0 ? TimeSpan.FromSeconds(granule / GranuleRate) : null;
            }

            pos = LastIndexOf(tail, PageMagic, pos);
        }

        return null;
    }

    private static bool TryGetPageEnd(byte[] buffer, int pageStart, out int pageEnd)
    {
        pageEnd = 0;
        if (pageStart + 27 > buffer.Length)
        {
            return false;
        }

        var segmentCount = buffer[pageStart + 26];
        var segmentTable = pageStart + 27;
        if (segmentTable + segmentCount > buffer.Length)
        {
            return false;
        }

        var payloadLength = 0;
        for (var i = 0; i < segmentCount; i++)
        {
            payloadLength += buffer[segmentTable + i];
        }

        pageEnd = segmentTable + segmentCount + payloadLength;
        return true;
    }

    private static IReadOnlyDictionary<string, string> ReadTags(byte[] head)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var start = IndexOf(head, TagsMagic, head.Length);
        if (start < 0)
        {
            return tags;
        }

        var offset = start + TagsMagic.Length;
        if (!TryReadUInt32(head, ref offset, out var vendorLength))
        {
            return tags;
        }

        offset += (int)vendorLength;
        if (!TryReadUInt32(head, ref offset, out var count))
        {
            return tags;
        }

        for (var i = 0; i < count; i++)
        {
            if (!TryReadUInt32(head, ref offset, out var commentLength) || offset + commentLength > head.Length)
            {
                break;
            }

            var comment = Encoding.UTF8.GetString(head, offset, (int)commentLength);
            offset += (int)commentLength;

            var separator = comment.IndexOf('=');
            if (separator > 0)
            {
                tags.TryAdd(comment[..separator], comment[(separator + 1)..]);
            }
        }

        return tags;
    }

    private static bool TryReadUInt32(byte[] buffer, ref int offset, out uint value)
    {
        value = 0;
        if (offset < 0 || offset + 4 > buffer.Length)
        {
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
        offset += 4;
        return true;
    }

    private static int IndexOf(byte[] buffer, byte[] pattern, int searchEnd) =>
        buffer.AsSpan(0, searchEnd).IndexOf(pattern);

    /// <summary>Last occurrence starting strictly before <paramref name="before"/>; -1 when none.</summary>
    private static int LastIndexOf(byte[] buffer, byte[] pattern, int before)
    {
        var limit = Math.Min(before, buffer.Length) - pattern.Length;
        for (var i = limit; i >= 0; i--)
        {
            if (buffer.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }
}
