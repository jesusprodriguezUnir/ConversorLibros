using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class ReaderEndpointsTests : IDisposable
{
    private const string IndexJson = """
        {"schemaVersion":1,"title":"Libro","chapters":[
          {"index":1,"title":"Uno","durationMs":4000,"audioKind":"opus"},
          {"index":2,"title":"Dos","durationMs":5000,"audioKind":"opus"}]}
        """;

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ConversorLibros.Web.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _outputRoot;
    private readonly string _bookDir;
    private readonly List<string> _junctions = [];

    public ReaderEndpointsTests()
    {
        _outputRoot = Path.Combine(_tempRoot, "library");
        _bookDir = Path.Combine(_outputRoot, "Libro");
        Directory.CreateDirectory(_bookDir);
    }

    public void Dispose()
    {
        // Remove junctions first: the recursive delete below fails on them.
        foreach (var link in _junctions.Where(Directory.Exists))
        {
            Directory.Delete(link);
        }

        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    // ---- Index and chapters

    [Fact]
    public async Task Index_BookWithReaderData_ReturnsAvailableWithChapters()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var response = await client.GetAsync($"/api/library/{id}/reader");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("chapters").GetArrayLength());
        Assert.False(doc.RootElement.GetProperty("hasCover").GetBoolean());
    }

    [Fact]
    public async Task Index_BookWithoutReaderData_ReturnsUnavailableWithReason()
    {
        File.WriteAllBytes(Path.Combine(_bookDir, "001 - Uno.opus"), [1]);
        var (client, id) = await StartAsync();

        using var response = await client.GetAsync($"/api/library/{id}/reader");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal("no-data", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Index_CorruptIndex_ReportsUnavailable()
    {
        SeedReader();
        File.WriteAllText(Path.Combine(_bookDir, "reader", "index.json"), "{ not json");
        var (client, id) = await StartAsync();

        using var response = await client.GetAsync($"/api/library/{id}/reader");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Chapter_InIndex_ReturnsItsJson()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var response = await client.GetAsync($"/api/library/{id}/reader/chapters/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, doc.RootElement.GetProperty("chapterIndex").GetInt32());
    }

    [Fact]
    public async Task Chapter_NotInIndex_ReturnsNotFound()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var response = await client.GetAsync($"/api/library/{id}/reader/chapters/9");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Chapter_CorruptJson_IsControlledErrorAndOthersStayReadable()
    {
        SeedReader();
        File.WriteAllText(Path.Combine(_bookDir, "reader", "chapter_002.json"), "{ broken");
        var (client, id) = await StartAsync();

        using var corrupt = await client.GetAsync($"/api/library/{id}/reader/chapters/2");
        using var healthy = await client.GetAsync($"/api/library/{id}/reader/chapters/1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, corrupt.StatusCode);
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
    }

    [Theory]
    [InlineData("..%2F..%2Fescape/reader")]
    [InlineData("unknown/reader")]
    [InlineData("unknown/reader/chapters/1")]
    [InlineData("unknown/chapters/1/audio")]
    [InlineData("unknown/reader/progress")]
    [InlineData("unknown/reader/bookmarks")]
    public async Task UnknownOrEncodedBookId_ReturnsNotFound(string path)
    {
        SeedReader();
        var (client, _) = await StartAsync();

        using var response = await client.GetAsync("/api/library/" + path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("reader/chapters/-1")]
    [InlineData("reader/chapters/abc")]
    [InlineData("chapters/-1/audio")]
    [InlineData("chapters/abc/audio")]
    [InlineData("reader/bookmarks/not-a-guid")]
    public async Task NegativeOrNonIntegerChapter_AndNonGuidBookmark_ReturnNotFound(string suffix)
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var response = await client.GetAsync($"/api/library/{id}/{suffix}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBookmark_NonGuidId_ReturnsNotFound()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var response = await client.DeleteAsync($"/api/library/{id}/reader/bookmarks/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Audio

    [Fact]
    public async Task Audio_FullRequest_ReturnsOggWithAcceptRanges()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var response = await client.GetAsync($"/api/library/{id}/chapters/1/audio");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/ogg", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("bytes", response.Headers.AcceptRanges);
        Assert.Equal(3000, (await response.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task Audio_RangeRequest_Returns206WithExactBytes()
    {
        SeedReader();
        var (client, id) = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/library/{id}/chapters/1/audio");
        request.Headers.Range = new RangeHeaderValue(1000, 1999);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(1000, response.Content.Headers.ContentRange?.To - response.Content.Headers.ContentRange?.From + 1);
        Assert.Equal(1000, (await response.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task Audio_RangeBeyondLength_Returns416()
    {
        SeedReader();
        var (client, id) = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/library/{id}/chapters/1/audio");
        request.Headers.Range = new RangeHeaderValue(5000, 6000);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
    }

    [Fact]
    public async Task Audio_MissingChapterFile_ReturnsNotFound()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var response = await client.GetAsync($"/api/library/{id}/chapters/2/audio");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Audio_M4bBook_ReturnsNotFound()
    {
        var m4bDir = Path.Combine(_outputRoot, "Otro");
        Directory.CreateDirectory(m4bDir);
        File.WriteAllBytes(Path.Combine(m4bDir, "Otro.m4b"), [1, 2, 3]);
        Directory.Delete(_bookDir, recursive: true);
        var (client, id) = await StartAsync();

        using var response = await client.GetAsync($"/api/library/{id}/chapters/1/audio");
        using var reader = await client.GetAsync($"/api/library/{id}/reader");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reader.StatusCode);
    }

    // ---- Progress

    [Fact]
    public async Task Progress_NoneStored_ReturnsNoContent()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var response = await client.GetAsync($"/api/library/{id}/reader/progress");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Progress_PutThenGet_RoundTrips()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var put = await client.PutAsJsonAsync($"/api/library/{id}/reader/progress", new { chapterIndex = 2, ms = 5000 });
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/library/{id}/reader/progress"));

        Assert.True(put.IsSuccessStatusCode);
        Assert.Equal(2, doc.RootElement.GetProperty("chapterIndex").GetInt32());
        Assert.Equal(5000, doc.RootElement.GetProperty("ms").GetInt64());
        Assert.True(doc.RootElement.TryGetProperty("updatedAt", out _));
    }

    [Theory]
    [InlineData(2, -1)]
    [InlineData(9, 0)]
    public async Task Progress_InvalidPayload_Returns400AndKeepsStoredValue(int chapterIndex, long ms)
    {
        SeedReader();
        var (client, id) = await StartAsync();
        await client.PutAsJsonAsync($"/api/library/{id}/reader/progress", new { chapterIndex = 1, ms = 100 });

        using var response = await client.PutAsJsonAsync($"/api/library/{id}/reader/progress", new { chapterIndex, ms });
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/api/library/{id}/reader/progress"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(100, doc.RootElement.GetProperty("ms").GetInt64());
    }

    [Fact]
    public async Task Progress_MalformedFile_IsEmptyAndNextWriteReplacesIt()
    {
        SeedReader();
        File.WriteAllText(Path.Combine(_bookDir, "reader", "progress.json"), "{ garbage");
        var (client, id) = await StartAsync();

        using var get = await client.GetAsync($"/api/library/{id}/reader/progress");
        using var put = await client.PutAsJsonAsync($"/api/library/{id}/reader/progress", new { chapterIndex = 1, ms = 7 });

        Assert.Equal(HttpStatusCode.NoContent, get.StatusCode);
        Assert.True(put.IsSuccessStatusCode);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_bookDir, "reader", "progress.json")));
        Assert.Equal(7, doc.RootElement.GetProperty("ms").GetInt64());
    }

    [Fact]
    public async Task Progress_ConcurrentPuts_LeaveValidJsonWithOneOfTheValues()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        var responses = await Task.WhenAll(Enumerable.Range(1, 20).Select(i =>
            client.PutAsJsonAsync($"/api/library/{id}/reader/progress", new { chapterIndex = 1, ms = i })));

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
        var path = Path.Combine(_bookDir, "reader", "progress.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.InRange(doc.RootElement.GetProperty("ms").GetInt64(), 1, 20);
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public async Task Progress_UnknownSchemaVersion_PutReturns409AndFileIsKept()
    {
        SeedReader();
        var path = Path.Combine(_bookDir, "reader", "progress.json");
        File.WriteAllText(path, """{"schemaVersion":2,"chapterIndex":1,"ms":1}""");
        var (client, id) = await StartAsync();

        using var get = await client.GetAsync($"/api/library/{id}/reader/progress");
        using var put = await client.PutAsJsonAsync($"/api/library/{id}/reader/progress", new { chapterIndex = 1, ms = 5 });

        Assert.Equal(HttpStatusCode.NoContent, get.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        Assert.Contains("\"schemaVersion\":2", File.ReadAllText(path));
    }

    // ---- Bookmarks

    [Fact]
    public async Task Bookmarks_CreateThenList_Returns201AndListsIt()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var created = await client.PostAsJsonAsync(
            $"/api/library/{id}/reader/bookmarks", new { chapterIndex = 1, ms = 1200, note = "idea", excerpt = "Hola" });
        var list = await client.GetFromJsonAsync<List<JsonElement>>($"/api/library/{id}/reader/bookmarks");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var item = Assert.Single(list!);
        Assert.Equal("idea", item.GetProperty("note").GetString());
        Assert.Equal(1200, item.GetProperty("ms").GetInt64());
        Assert.True(Guid.TryParse(item.GetProperty("id").GetString(), out _));
    }

    [Fact]
    public async Task Bookmarks_UpdateNoteThenDelete_Works()
    {
        SeedReader();
        var (client, id) = await StartAsync();
        var created = await (await client.PostAsJsonAsync(
            $"/api/library/{id}/reader/bookmarks", new { chapterIndex = 2, ms = 10 })).Content.ReadFromJsonAsync<JsonElement>();
        var bookmarkId = created.GetProperty("id").GetString();

        using var update = await client.PutAsJsonAsync($"/api/library/{id}/reader/bookmarks/{bookmarkId}", new { note = "nueva" });
        var afterUpdate = await client.GetFromJsonAsync<List<JsonElement>>($"/api/library/{id}/reader/bookmarks");
        using var delete = await client.DeleteAsync($"/api/library/{id}/reader/bookmarks/{bookmarkId}");
        var afterDelete = await client.GetFromJsonAsync<List<JsonElement>>($"/api/library/{id}/reader/bookmarks");

        Assert.True(update.IsSuccessStatusCode);
        Assert.Equal("nueva", Assert.Single(afterUpdate!).GetProperty("note").GetString());
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(afterDelete!);
    }

    [Fact]
    public async Task Bookmarks_UnknownId_DeleteAndUpdateReturn404()
    {
        SeedReader();
        var (client, id) = await StartAsync();
        var unknown = Guid.NewGuid();

        using var delete = await client.DeleteAsync($"/api/library/{id}/reader/bookmarks/{unknown}");
        using var update = await client.PutAsJsonAsync($"/api/library/{id}/reader/bookmarks/{unknown}", new { note = "x" });

        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
    }

    [Fact]
    public async Task Bookmarks_NoteOver500_Returns400()
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var response = await client.PostAsJsonAsync(
            $"/api/library/{id}/reader/bookmarks", new { chapterIndex = 1, ms = 0, note = new string('a', 501) });
        var list = await client.GetFromJsonAsync<List<JsonElement>>($"/api/library/{id}/reader/bookmarks");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(list!);
    }

    [Theory]
    [InlineData(1, -5)]
    [InlineData(9, 0)]
    public async Task Bookmarks_InvalidChapterOrMs_Returns400(int chapterIndex, long ms)
    {
        SeedReader();
        var (client, id) = await StartAsync();

        using var response = await client.PostAsJsonAsync($"/api/library/{id}/reader/bookmarks", new { chapterIndex, ms });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bookmarks_MalformedFile_ListsEmptyAndNextWriteReplacesIt()
    {
        SeedReader();
        File.WriteAllText(Path.Combine(_bookDir, "reader", "bookmarks.json"), "[[[");
        var (client, id) = await StartAsync();

        var list = await client.GetFromJsonAsync<List<JsonElement>>($"/api/library/{id}/reader/bookmarks");
        using var created = await client.PostAsJsonAsync($"/api/library/{id}/reader/bookmarks", new { chapterIndex = 1, ms = 1 });

        Assert.Empty(list!);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task Bookmarks_UnknownSchemaVersion_PostReturns409()
    {
        SeedReader();
        File.WriteAllText(Path.Combine(_bookDir, "reader", "bookmarks.json"), """{"schemaVersion":2,"bookmarks":[]}""");
        var (client, id) = await StartAsync();

        using var response = await client.PostAsJsonAsync($"/api/library/{id}/reader/bookmarks", new { chapterIndex = 1, ms = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Bookmarks_FileWithNullEntry_ListsOnlyValidBookmarks()
    {
        SeedReader();
        File.WriteAllText(
            Path.Combine(_bookDir, "reader", "bookmarks.json"),
            """{"schemaVersion":1,"bookmarks":[null,{"id":"6f1c2b4e-8a2d-4c1e-9b3f-0d5e7a9c1b2d","chapterIndex":1,"ms":5}]}""");
        var (client, id) = await StartAsync();

        var list = await client.GetFromJsonAsync<List<JsonElement>>($"/api/library/{id}/reader/bookmarks");
        using var created = await client.PostAsJsonAsync($"/api/library/{id}/reader/bookmarks", new { chapterIndex = 1, ms = 1 });

        Assert.Single(list!);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    // ---- Links (containment must hold after resolving junctions)

    [Fact]
    public async Task Audio_BookFolderIsJunctionOutsideOutputRoot_ReturnsNotFound()
    {
        SeedReader();
        var (client, _) = await StartAsync();
        var outside = Path.Combine(_tempRoot, "outside");
        Directory.Move(_bookDir, outside);
        CreateJunction(_bookDir, outside);
        var id = LibraryIds.Compute(_bookDir);

        using var audio = await client.GetAsync($"/api/library/{id}/chapters/1/audio");
        using var reader = await client.GetAsync($"/api/library/{id}/reader");

        Assert.Equal(HttpStatusCode.NotFound, audio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reader.StatusCode);
    }

    [Fact]
    public async Task Reader_ReaderFolderIsJunctionOutsideOutputRoot_ReturnsNotFound()
    {
        SeedReader();
        var (client, id) = await StartAsync();
        var readerDir = Path.Combine(_bookDir, "reader");
        var outside = Path.Combine(_tempRoot, "outside-reader");
        Directory.Move(readerDir, outside);
        CreateJunction(readerDir, outside);

        using var index = await client.GetAsync($"/api/library/{id}/reader");
        using var progress = await client.PutAsJsonAsync($"/api/library/{id}/reader/progress", new { chapterIndex = 1, ms = 1 });

        Assert.Equal(HttpStatusCode.NotFound, index.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, progress.StatusCode);
        Assert.False(File.Exists(Path.Combine(outside, "progress.json")));
    }

    // ---- Helpers

    private void CreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        process.WaitForExit();
        _junctions.Add(link);
        Assert.True(new DirectoryInfo(link).LinkTarget is not null, $"Could not create junction {link}.");
    }

    private void SeedReader()
    {
        var readerDir = Path.Combine(_bookDir, "reader");
        Directory.CreateDirectory(readerDir);
        File.WriteAllBytes(Path.Combine(_bookDir, "001 - Uno.opus"), new byte[3000]);
        File.WriteAllText(Path.Combine(readerDir, "index.json"), IndexJson);
        File.WriteAllText(Path.Combine(readerDir, "chapter_001.json"), """{"schemaVersion":1,"chapterIndex":1,"title":"Uno","durationMs":4000,"chunks":[],"paragraphs":[]}""");
        File.WriteAllText(Path.Combine(readerDir, "chapter_002.json"), """{"schemaVersion":1,"chapterIndex":2,"title":"Dos","durationMs":5000,"chunks":[],"paragraphs":[]}""");
    }

    private async Task<(HttpClient Client, string Id)> StartAsync()
    {
        var settingsPath = Path.Combine(_tempRoot, "settings.json");
        var settingsStore = new JsonSettingsStore(settingsPath);
        settingsStore.Save(new WebSettings("", _outputRoot, JsonSettingsStore.DefaultOllamaUrl, JsonSettingsStore.DefaultOllamaModel, ""));
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(settingsStore);
                services.AddSingleton(new UploadedBookStore(Path.Combine(_tempRoot, "uploads")));
            }));
        var client = factory.CreateClient();
        var items = await client.GetFromJsonAsync<List<LibraryItemDto>>("/api/library");
        return (client, Assert.Single(items!).Id);
    }
}
