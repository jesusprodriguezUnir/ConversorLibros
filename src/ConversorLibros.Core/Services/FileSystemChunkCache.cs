using ConversorLibros.Core.Abstractions;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Persists synthesized chunk WAV files on disk keyed by their content hash.
/// </summary>
public sealed class FileSystemChunkCache : IChunkCache
{
    private readonly string _cacheDirectory;

    public FileSystemChunkCache(string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new ArgumentException("Cache directory cannot be empty.", nameof(cacheDirectory));
        }

        _cacheDirectory = cacheDirectory;
    }

    /// <summary>Smallest valid WAV: a 44-byte header with no PCM samples.</summary>
    private const int WavHeaderBytes = 44;

    public Task<string?> TryGetAudioPathAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(contentHash);
        // Files without PCM data are treated as misses so they get re-synthesized.
        var isValid = File.Exists(path) && new FileInfo(path).Length > WavHeaderBytes;
        return Task.FromResult(isValid ? path : null);
    }

    public async Task<string> SaveAudioAsync(string contentHash, byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wavBytes);
        if (wavBytes.Length <= WavHeaderBytes)
        {
            throw new InvalidOperationException($"Refusing to cache WAV without PCM data for chunk '{contentHash}'.");
        }

        Directory.CreateDirectory(_cacheDirectory);
        var path = GetPath(contentHash);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        await File.WriteAllBytesAsync(tempPath, wavBytes, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);

        return path;
    }

    private string GetPath(string contentHash) => Path.Combine(_cacheDirectory, $"{contentHash}.wav");
}
