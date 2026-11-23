using System.Text;
using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Services;

/// <summary>Reads PCM WAV headers without decoding audio.</summary>
public static class WavFile
{
    private const int MinHeaderLength = 44;
    private const ushort PcmFormat = 1;

    /// <summary>
    /// PCM facts from the RIFF fmt and data chunks. Throws <see cref="InvalidDataException"/> for files that are
    /// truncated, not RIFF/WAVE, not integer PCM, or lack a fmt/data chunk.
    /// </summary>
    public static WavPcmInfo ReadPcmInfo(string wavPath)
    {
        using var stream = File.OpenRead(wavPath);
        if (stream.Length < MinHeaderLength)
        {
            throw new InvalidDataException($"'{wavPath}' is shorter than a WAV header.");
        }

        try
        {
            using var reader = new BinaryReader(stream);
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
            {
                throw new InvalidDataException($"'{wavPath}' is not a RIFF WAV file.");
            }

            reader.ReadUInt32();
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
            {
                throw new InvalidDataException($"'{wavPath}' is not a WAVE file.");
            }

            WavPcmInfo? format = null;
            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                var chunkSize = reader.ReadUInt32();

                if (chunkId == "fmt ")
                {
                    var audioFormat = reader.ReadUInt16();
                    var channels = reader.ReadUInt16();
                    var sampleRate = reader.ReadUInt32();
                    var byteRate = reader.ReadUInt32();
                    reader.ReadUInt16(); // block align
                    var bits = reader.ReadUInt16();
                    if (audioFormat != PcmFormat || byteRate == 0)
                    {
                        throw new InvalidDataException($"'{wavPath}' is not integer PCM audio.");
                    }

                    format = new WavPcmInfo((int)sampleRate, channels, bits, (int)byteRate, 0, 0);
                    stream.Position += chunkSize - 16 + (chunkSize % 2);
                }
                else if (chunkId == "data")
                {
                    if (format is null)
                    {
                        throw new InvalidDataException($"'{wavPath}' has a data chunk before its fmt chunk.");
                    }

                    var dataBytes = Math.Min(chunkSize, stream.Length - stream.Position);
                    return format with { DataOffset = stream.Position, DataBytes = dataBytes };
                }
                else
                {
                    stream.Position += chunkSize + (chunkSize % 2);
                }
            }
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"'{wavPath}' has a truncated header.", ex);
        }

        throw new InvalidDataException($"'{wavPath}' has no data chunk.");
    }

    /// <summary>
    /// Duration from the RIFF fmt byte rate and the data chunk size. Streamed WAVs may carry a placeholder
    /// size, so the actual file length is used when it is smaller.
    /// </summary>
    public static TimeSpan ReadDuration(string wavPath)
    {
        using var stream = File.OpenRead(wavPath);
        using var reader = new BinaryReader(stream);

        if (stream.Length < 12 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
        {
            throw new InvalidDataException($"'{wavPath}' is not a RIFF WAV file.");
        }

        reader.ReadUInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
        {
            throw new InvalidDataException($"'{wavPath}' is not a WAVE file.");
        }

        uint byteRate = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunkSize = reader.ReadUInt32();

            if (chunkId == "fmt ")
            {
                reader.ReadUInt16(); // audio format
                reader.ReadUInt16(); // channels
                reader.ReadUInt32(); // sample rate
                byteRate = reader.ReadUInt32();
                stream.Position += chunkSize - 12;
            }
            else if (chunkId == "data")
            {
                if (byteRate == 0)
                {
                    throw new InvalidDataException($"'{wavPath}' has a data chunk before its fmt chunk.");
                }

                var dataBytes = Math.Min(chunkSize, stream.Length - stream.Position);
                return TimeSpan.FromSeconds((double)dataBytes / byteRate);
            }
            else
            {
                stream.Position += chunkSize + (chunkSize % 2);
            }
        }

        throw new InvalidDataException($"'{wavPath}' has no data chunk.");
    }
}
