using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class LibraryDownloadAndSettingsTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ConversorLibros.Web.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _outputRoot;

    public LibraryDownloadAndSettingsTests()
    {
        _outputRoot = Path.Combine(_tempRoot, "library");
        Directory.CreateDirectory(_outputRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Download_OpusFolderStreamsZipWithOnlyOpusAndCover()
    {
        var bookDir = Path.Combine(_outputRoot, "Canción");
        Directory.CreateDirectory(Path.Combine(bookDir, "work", "cache"));
        File.WriteAllBytes(Path.Combine(bookDir, "001 - Inicio.opus"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(bookDir, "cover.jpg"), [4, 5]);
        File.WriteAllBytes(Path.Combine(bookDir, "work", "chapter_001.wav"), [9, 9, 9]);
        File.WriteAllBytes(Path.Combine(bookDir, "work", "cache", "chunk.opus"), [8]);

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var id = await GetFirstIdAsync(client);

        using var response = await client.GetAsync($"/api/library/{id}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("Canción.zip", disposition.FileNameStar);
        Assert.True(disposition.FileName?.All(c => c < 128) ?? false, "El nombre ASCII de respaldo debe ser ASCII puro.");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(["001 - Inicio.opus", "cover.jpg"], names);
    }

    [Fact]
    public async Task Download_NestedM4bStreamsFileWithAttachmentHeader()
    {
        var bookDir = Path.Combine(_outputRoot, "Libro");
        Directory.CreateDirectory(Path.Combine(bookDir, "work"));
        File.WriteAllBytes(Path.Combine(bookDir, "Libro.m4b"), [7, 7, 7]);

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var id = await GetFirstIdAsync(client);

        using var response = await client.GetAsync($"/api/library/{id}/download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Libro.m4b", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(new byte[] { 7, 7, 7 }, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Download_UnknownIdReturnsNotFound()
    {
        Directory.CreateDirectory(Path.Combine(_outputRoot, "Libro"));
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/library/..%2F..%2Fescape/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void OldSettingsJsonWithoutDefaultsGetsSpeedAndFormatDefaults()
    {
        var settingsPath = Path.Combine(_tempRoot, "settings.json");
        File.WriteAllText(settingsPath, """
            {
              "piperModelDir": "",
              "outputRoot": "C:\\out",
              "ollamaUrl": "http://localhost:11434",
              "ollamaModel": "qwen",
              "ffmpegOverride": ""
            }
            """);

        var settings = new JsonSettingsStore(settingsPath).Get();

        Assert.Equal(1.0f, settings.DefaultSpeed);
        Assert.Equal("opus", settings.DefaultFormat);
    }

    [Fact]
    public void Save_NormalizesInvalidSpeedAndFormat()
    {
        var settingsPath = Path.Combine(_tempRoot, "settings.json");
        var store = new JsonSettingsStore(settingsPath);

        var saved = store.Save(new WebSettings("", _outputRoot, "http://localhost:11434", "m", "", 0f, "wav"));

        Assert.Equal(1.0f, saved.DefaultSpeed);
        Assert.Equal("opus", saved.DefaultFormat);
        Assert.Equal(1.0f, store.Get().DefaultSpeed);
    }

    [Fact]
    public void AutoDetectedModelIsReportedButNeverPersisted()
    {
        var appDir = Path.Combine(_tempRoot, "app");
        var modelDir = Path.Combine(appDir, "models", "voice");
        Directory.CreateDirectory(Path.Combine(modelDir, "espeak-ng-data"));
        File.WriteAllText(Path.Combine(modelDir, "voice.onnx"), "onnx");
        File.WriteAllText(Path.Combine(modelDir, "tokens.txt"), "tokens");

        var settingsPath = Path.Combine(_tempRoot, "settings.json");
        var locator = new PiperModelLocator(Path.Combine(appDir, "bin"), null, null);
        var store = new JsonSettingsStore(settingsPath, locator);

        store.Save(new WebSettings("", _outputRoot, "http://localhost:11434", "m", ""));
        var response = store.GetResponse();

        Assert.Equal(modelDir, response.EffectivePiperModelDir);
        Assert.True(response.PiperAutoDetected);
        Assert.Equal("", response.Settings.PiperModelDir);
        Assert.DoesNotContain(modelDir, File.ReadAllText(settingsPath));
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var settingsPath = Path.Combine(_tempRoot, "settings.json");
        var settingsStore = new JsonSettingsStore(settingsPath);
        settingsStore.Save(new WebSettings("", _outputRoot, JsonSettingsStore.DefaultOllamaUrl, JsonSettingsStore.DefaultOllamaModel, ""));

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(settingsStore);
                services.AddSingleton(new UploadedBookStore(Path.Combine(_tempRoot, "uploads")));
            });
        });
    }

    private static async Task<string> GetFirstIdAsync(HttpClient client)
    {
        var items = await client.GetFromJsonAsync<List<LibraryItemDto>>("/api/library");
        Assert.NotNull(items);
        return Assert.Single(items).Id;
    }
}
