using System.Net;
using System.Net.Http.Json;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using ConversorLibros.Web.Services.Library;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class SettingsAndFileSystemTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ConversorLibros.Web.Tests", Guid.NewGuid().ToString("N"));

    public SettingsAndFileSystemTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void NormalizePiperDir_SubfolderMapsToParentContainingOnnx()
    {
        var modelDir = Path.Combine(_tempRoot, "piper-model");
        var espeakDir = Path.Combine(modelDir, "espeak-ng-data");
        Directory.CreateDirectory(espeakDir);
        File.WriteAllText(Path.Combine(modelDir, "voice.onnx"), "dummy");

        var normalized = JsonSettingsStore.NormalizeModelDir(espeakDir);

        Assert.Equal(modelDir, normalized);
    }

    [Fact]
    public void ValidatePiper_MissingFilesReportsExactMissingItems()
    {
        var emptyDir = Path.Combine(_tempRoot, "empty-model");
        Directory.CreateDirectory(emptyDir);

        var status = JsonSettingsStore.ValidatePiper(emptyDir);

        Assert.False(status.IsValid);
        Assert.Contains(".onnx", status.Message);
        Assert.Contains("tokens.txt", status.Message);
        Assert.Contains("espeak-ng-data", status.Message);
    }

    [Fact]
    public void SaveAndGet_PersistsSettingsToJsonFile()
    {
        var settingsPath = Path.Combine(_tempRoot, "settings.json");
        var store = new JsonSettingsStore(settingsPath);
        var modelDir = Path.Combine(_tempRoot, "voice-model");
        var espeakDir = Path.Combine(modelDir, "espeak-ng-data");
        Directory.CreateDirectory(espeakDir);
        File.WriteAllText(Path.Combine(modelDir, "es_ES.onnx"), "onnx");
        File.WriteAllText(Path.Combine(modelDir, "tokens.txt"), "tokens");

        var saved = store.Save(new WebSettings(
            PiperModelDir: espeakDir,
            OutputRoot: Path.Combine(_tempRoot, "out"),
            OllamaUrl: "http://localhost:11434",
            OllamaModel: "qwen3:8b",
            FfmpegOverride: ""));

        Assert.Equal(modelDir, saved.PiperModelDir);

        var reloaded = new JsonSettingsStore(settingsPath).Get();
        Assert.Equal(modelDir, reloaded.PiperModelDir);
        Assert.Equal(Path.Combine(_tempRoot, "out"), reloaded.OutputRoot);
        Assert.Equal("qwen3:8b", reloaded.OllamaModel);
    }

    [Fact]
    public async Task GetBrowse_InvalidPathReturnsBadRequest()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var missingPath = Path.Combine(_tempRoot, "does-not-exist-xyz");
        var response = await client.GetAsync($"/api/fs/browse?path={Uri.EscapeDataString(missingPath)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBrowse_ValidDirectoryListsSubdirectoriesAndParent()
    {
        var subA = Path.Combine(_tempRoot, "alpha");
        var subB = Path.Combine(_tempRoot, "beta");
        Directory.CreateDirectory(subA);
        Directory.CreateDirectory(subB);

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/fs/browse?path={Uri.EscapeDataString(_tempRoot)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FileSystemBrowseResponse>();
        Assert.NotNull(payload);
        Assert.Equal(_tempRoot, payload.CurrentPath);
        Assert.NotNull(payload.ParentPath);
        Assert.Contains(payload.Directories, d => d.Name == "alpha" && d.Path == subA);
        Assert.Contains(payload.Directories, d => d.Name == "beta" && d.Path == subB);
    }

    [Fact]
    public async Task GetBrowse_EmptyPathReturnsLogicalDrives()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/fs/browse");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FileSystemBrowseResponse>();
        Assert.NotNull(payload);
        Assert.Null(payload.CurrentPath);
        Assert.NotEmpty(payload.Directories);
    }

    [Fact]
    public async Task TestOllama_UnreachableEndpointReturnsConnectedFalse()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/settings/test-ollama",
            new TestOllamaRequest("http://127.0.0.1:1", "qwen3:8b"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TestOllamaResponse>();
        Assert.NotNull(payload);
        Assert.False(payload.Connected);
        Assert.False(payload.ModelInstalled);
    }

    [Fact]
    public async Task PutSettings_PersistsBooksRoot()
    {
        var outputRoot = Path.Combine(_tempRoot, "out-books-root");
        var booksRoot = Path.Combine(_tempRoot, "my-epubs");
        Directory.CreateDirectory(outputRoot);
        Directory.CreateDirectory(booksRoot);

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var putResponse = await client.PutAsJsonAsync(
            "/api/settings",
            new WebSettings(
                PiperModelDir: "",
                OutputRoot: outputRoot,
                OllamaUrl: JsonSettingsStore.DefaultOllamaUrl,
                OllamaModel: JsonSettingsStore.DefaultOllamaModel,
                FfmpegOverride: "",
                BooksRoot: booksRoot));

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        var saved = await putResponse.Content.ReadFromJsonAsync<SettingsResponse>();
        Assert.NotNull(saved);
        Assert.Equal(booksRoot, saved.Settings.BooksRoot);

        var getResponse = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");
        Assert.NotNull(getResponse);
        Assert.Equal(booksRoot, getResponse.Settings.BooksRoot);
    }

    [Fact]
    public async Task PutSettings_ChangingOutputRootWhileJobActive_Returns409AndDoesNotSave()
    {
        var initialOutputRoot = Path.Combine(_tempRoot, "out-initial");
        var newOutputRoot = Path.Combine(_tempRoot, "out-changed");
        Directory.CreateDirectory(initialOutputRoot);
        Directory.CreateDirectory(newOutputRoot);

        var settingsPath = Path.Combine(_tempRoot, "guard-settings.json");
        var settingsStore = new JsonSettingsStore(settingsPath);
        settingsStore.Save(new WebSettings(
            PiperModelDir: "",
            OutputRoot: initialOutputRoot,
            OllamaUrl: JsonSettingsStore.DefaultOllamaUrl,
            OllamaModel: JsonSettingsStore.DefaultOllamaModel,
            FfmpegOverride: ""));

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(settingsStore);
            });
        });

        var libraryContext = factory.Services.GetRequiredService<ILibraryContext>();
        using (var conn = libraryContext.OpenConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO books (id, title, sort_title, created_at, updated_at)
                VALUES ('book-1', 'Test', 'Test', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
                INSERT INTO conversion_jobs (id, book_id, status, options_json, created_at)
                VALUES ('job-1', 'book-1', 'queued', '{}', '2026-01-01T00:00:00Z');
                """;
            cmd.ExecuteNonQuery();
        }

        using var client = factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            "/api/settings",
            new WebSettings(
                PiperModelDir: "",
                OutputRoot: newOutputRoot,
                OllamaUrl: JsonSettingsStore.DefaultOllamaUrl,
                OllamaModel: JsonSettingsStore.DefaultOllamaModel,
                FfmpegOverride: ""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(initialOutputRoot, settingsStore.Get().OutputRoot);
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var settingsPath = Path.Combine(_tempRoot, "api-settings.json");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(new JsonSettingsStore(settingsPath));
            });
        });
    }
}
