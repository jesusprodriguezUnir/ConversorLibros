using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class FileSystemChunkCacheTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), $"conversor_ccache_{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAudioAsync_HeaderOnlyWav_Throws()
    {
        var sut = new FileSystemChunkCache(_cacheDir);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SaveAudioAsync("hash-empty", new byte[44]));
    }

    [Fact]
    public async Task TryGetAudioPathAsync_HeaderOnlyFile_IsTreatedAsMiss()
    {
        Directory.CreateDirectory(_cacheDir);
        await File.WriteAllBytesAsync(Path.Combine(_cacheDir, "hash-poisoned.wav"), new byte[44]);
        var sut = new FileSystemChunkCache(_cacheDir);

        Assert.Null(await sut.TryGetAudioPathAsync("hash-poisoned"));
    }

    [Fact]
    public async Task SaveAudioAsync_ValidWav_IsReturnedByTryGet()
    {
        var sut = new FileSystemChunkCache(_cacheDir);
        var wav = new byte[44 + 16];

        var path = await sut.SaveAudioAsync("hash-ok", wav);

        Assert.Equal(path, await sut.TryGetAudioPathAsync("hash-ok"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }
}
