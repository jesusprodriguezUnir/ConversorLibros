using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Computes chunk timings inside an assembled chapter. Mirrors <see cref="WavChapterAssembler"/>, which keeps
/// the first chunk header and strips exactly 44 bytes from every chunk, so each chunk contributes
/// <c>fileLength - 44</c> data bytes. Milliseconds come from cumulative bytes, so rounding never drifts.
/// </summary>
public static class SyncMapBuilder
{
    private const int WavHeaderSize = 44;

    /// <summary>
    /// Returns null when a chunk WAV cannot be read or its byte rate differs from the first chunk
    /// (the assembler would have concatenated incompatible audio, so timings cannot be trusted).
    /// </summary>
    public static ChapterSyncMap? Build(Chapter chapter, IReadOnlyList<TextChunk> chunks, IReadOnlyList<string> chunkPaths)
    {
        if (chunks.Count != chunkPaths.Count)
        {
            throw new ArgumentException("Chunk and audio path counts must match.", nameof(chunkPaths));
        }

        var timings = new List<ChunkTiming>(chunks.Count);
        long cumulativeBytes = 0;
        long cumulativePauseMs = 0;
        long cursorMs = 0;
        int byteRate = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            long dataBytes;
            try
            {
                var info = WavFile.ReadPcmInfo(chunkPaths[i]);
                if (i == 0)
                {
                    byteRate = info.ByteRate;
                }
                else if (info.ByteRate != byteRate)
                {
                    return null;
                }

                dataBytes = Math.Max(0, new FileInfo(chunkPaths[i]).Length - WavHeaderSize);
            }
            catch (InvalidDataException)
            {
                return null;
            }

            var startMs = cursorMs;
            cumulativeBytes += dataBytes;
            var endMs = ToMilliseconds(cumulativeBytes, byteRate) + cumulativePauseMs;
            timings.Add(new ChunkTiming(chunks[i].Order, chunks[i].NormalizedText, startMs, endMs));

            var pauseMs = chunks[i].PauseAfter > TimeSpan.Zero
                ? (long)Math.Round(chunks[i].PauseAfter.TotalMilliseconds)
                : 0;
            cumulativePauseMs += pauseMs;
            cursorMs = endMs + pauseMs;
        }

        return new ChapterSyncMap(chapter.Index, chapter.Title, chapter.RawText, cursorMs, timings);
    }

    private static long ToMilliseconds(long bytes, int byteRate) =>
        (bytes * 1000 + byteRate / 2) / byteRate;
}
