using System.Text;

namespace ConversorLibros.Core.Services;

/// <summary>Reads PCM WAV headers without decoding audio.</summary>
public static class WavFile
{
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
