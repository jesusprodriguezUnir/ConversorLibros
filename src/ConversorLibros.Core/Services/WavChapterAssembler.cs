using System.Buffers.Binary;
using ConversorLibros.Core.Abstractions;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Concatenates chunk WAV files into a single chapter WAV file using streaming I/O
/// to avoid loading full chapter audio into memory.
/// </summary>
public sealed class WavChapterAssembler : IChapterAssembler
{
    private const int WavHeaderSize = 44;

    public async Task<string> AssembleChapterAsync(
        int chapterIndex,
        IReadOnlyList<string> chunkAudioPaths,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        if (chunkAudioPaths.Count == 0)
        {
            throw new ArgumentException("At least one chunk audio path is required.", nameof(chunkAudioPaths));
        }

        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"chapter_{chapterIndex:D3}.wav");
        var tempPath = $"{outputPath}.{Guid.NewGuid():N}.tmp";

        long totalDataBytes = 0;

        await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, useAsync: true))
        {
            // Reserve 44-byte WAV header
            var header = new byte[WavHeaderSize];
            await using (var first = new FileStream(chunkAudioPaths[0], FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                var read = await first.ReadAsync(header.AsMemory(0, WavHeaderSize), cancellationToken).ConfigureAwait(false);
                if (read < WavHeaderSize)
                {
                    throw new InvalidDataException($"Invalid WAV header in '{chunkAudioPaths[0]}'.");
                }

                await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                totalDataBytes += await CopyRemainingAsync(first, output, cancellationToken).ConfigureAwait(false);
            }

            for (var i = 1; i < chunkAudioPaths.Count; i++)
            {
                await using var chunkStream = new FileStream(chunkAudioPaths[i], FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                if (chunkStream.Length > WavHeaderSize)
                {
                    chunkStream.Seek(WavHeaderSize, SeekOrigin.Begin);
                    totalDataBytes += await CopyRemainingAsync(chunkStream, output, cancellationToken).ConfigureAwait(false);
                }
            }

            // Update RIFF chunk size (offset 4) and data subchunk size (offset 40)
            var sizeBuffer = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(sizeBuffer, (uint)(36 + totalDataBytes));
            output.Seek(4, SeekOrigin.Begin);
            await output.WriteAsync(sizeBuffer, cancellationToken).ConfigureAwait(false);

            BinaryPrimitives.WriteUInt32LittleEndian(sizeBuffer, (uint)totalDataBytes);
            output.Seek(40, SeekOrigin.Begin);
            await output.WriteAsync(sizeBuffer, cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, outputPath, overwrite: true);
        return outputPath;
    }

    private static async Task<long> CopyRemainingAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            copied += bytesRead;
        }

        return copied;
    }
}
