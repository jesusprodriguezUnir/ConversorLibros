using System.Text;
using ConversorLibros.Core.Abstractions;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Persists translated text fragments on disk, one UTF-8 text file per cache key.
/// </summary>
public sealed class FileSystemTranslationCache : ITranslationCache
{
    private readonly string _cacheDirectory;

    public FileSystemTranslationCache(string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new ArgumentException("Cache directory cannot be empty.", nameof(cacheDirectory));
        }

        _cacheDirectory = cacheDirectory;
    }

    public async Task<string?> TryGetAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public async Task SaveAsync(string key, string translatedText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return;
        }

        Directory.CreateDirectory(_cacheDirectory);
        var path = GetPath(key);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        await File.WriteAllTextAsync(tempPath, translatedText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    private string GetPath(string key) => Path.Combine(_cacheDirectory, $"{key}.txt");
}
