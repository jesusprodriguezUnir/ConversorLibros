using ConversorLibros.Web.Services;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class PiperModelLocatorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ConversorLibros.Web.Tests", Guid.NewGuid().ToString("N"));

    public PiperModelLocatorTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ValidConfiguredDirWinsWithoutAutoDetection()
    {
        var configured = CreateModel(Path.Combine(_tempRoot, "configured"));
        CreateModel(Path.Combine(_tempRoot, "app", "models", "other"));
        var locator = new PiperModelLocator(Path.Combine(_tempRoot, "app"), null, null);

        var resolution = locator.Resolve(configured);

        Assert.Equal(configured, resolution.EffectiveDir);
        Assert.False(resolution.IsAutoDetected);
        Assert.True(resolution.Status.IsValid);
    }

    [Fact]
    public void Resolve_InvalidConfiguredDirFallsBackToNearestModelsAncestor()
    {
        var baseDir = Path.Combine(_tempRoot, "app", "bin", "Debug");
        Directory.CreateDirectory(baseDir);
        var near = CreateModel(Path.Combine(_tempRoot, "app", "models", "near"));
        CreateModel(Path.Combine(_tempRoot, "models", "far"));
        var locator = new PiperModelLocator(baseDir, null, null);

        var resolution = locator.Resolve(Path.Combine(_tempRoot, "missing"));

        Assert.Equal(near, resolution.EffectiveDir);
        Assert.True(resolution.IsAutoDetected);
        Assert.True(resolution.Status.IsValid);
    }

    [Fact]
    public void Resolve_SkipsInvalidCandidatesAndPicksFirstValidInOrdinalOrder()
    {
        var modelsRoot = Path.Combine(_tempRoot, "app", "models");
        Directory.CreateDirectory(Path.Combine(modelsRoot, "aaa-broken"));
        var good = CreateModel(Path.Combine(modelsRoot, "bbb-good"));
        CreateModel(Path.Combine(modelsRoot, "ccc-also-good"));
        var locator = new PiperModelLocator(Path.Combine(_tempRoot, "app"), null, null);

        var resolution = locator.Resolve(string.Empty);

        Assert.Equal(good, resolution.EffectiveDir);
        Assert.True(resolution.IsAutoDetected);
    }

    [Fact]
    public void Resolve_ProbesLocalAppDataAfterAncestors()
    {
        var localAppData = Path.Combine(_tempRoot, "local");
        var fromLocal = CreateModel(Path.Combine(localAppData, "ConversorLibros", "models", "voice"));
        var locator = new PiperModelLocator(Path.Combine(_tempRoot, "app"), null, localAppData);

        var resolution = locator.Resolve(string.Empty);

        Assert.Equal(fromLocal, resolution.EffectiveDir);
        Assert.True(resolution.IsAutoDetected);
    }

    [Fact]
    public void Resolve_NothingFoundReturnsConfiguredStatusWithoutAutoDetection()
    {
        var locator = new PiperModelLocator(Path.Combine(_tempRoot, "app"), null, Path.Combine(_tempRoot, "local"));
        var missing = Path.Combine(_tempRoot, "missing");

        var resolution = locator.Resolve(missing);

        Assert.Equal(missing, resolution.EffectiveDir);
        Assert.False(resolution.IsAutoDetected);
        Assert.False(resolution.Status.IsValid);
    }

    private static string CreateModel(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "espeak-ng-data"));
        File.WriteAllText(Path.Combine(dir, "voice.onnx"), "onnx");
        File.WriteAllText(Path.Combine(dir, "tokens.txt"), "tokens");
        return dir;
    }
}
