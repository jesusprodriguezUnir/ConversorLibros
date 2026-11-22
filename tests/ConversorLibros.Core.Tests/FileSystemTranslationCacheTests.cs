using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class FileSystemTranslationCacheTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"conversor_tcache_{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_ThenTryGetAsync_RoundTripsText()
    {
        var sut = new FileSystemTranslationCache(_cacheDir);

        await sut.SaveAsync("key1", "Hola, ¿qué tal? Árbol.");
        var result = await sut.TryGetAsync("key1");

        Assert.Equal("Hola, ¿qué tal? Árbol.", result);
    }

    [Fact]
    public async Task SaveAsync_EmptyText_IsNotPersisted()
    {
        var sut = new FileSystemTranslationCache(_cacheDir);

        await sut.SaveAsync("key-empty", "   ");

        Assert.Null(await sut.TryGetAsync("key-empty"));
        Assert.False(Directory.Exists(_cacheDir));
    }

    [Fact]
    public async Task TryGetAsync_EmptyFile_IsTreatedAsMiss()
    {
        Directory.CreateDirectory(_cacheDir);
        await File.WriteAllTextAsync(Path.Combine(_cacheDir, "key-blank.txt"), "");
        var sut = new FileSystemTranslationCache(_cacheDir);

        Assert.Null(await sut.TryGetAsync("key-blank"));
    }

    [Fact]
    public async Task TryGetAsync_MissingKey_ReturnsNull()
    {
        var sut = new FileSystemTranslationCache(_cacheDir);

        Assert.Null(await sut.TryGetAsync("does-not-exist"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }
}
