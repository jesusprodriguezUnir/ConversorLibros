using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using ConversorLibros.Web.Services.Library;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class CatalogEndpointsTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "ConversorLibros.Web.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _outputRoot;
    private readonly string _booksRoot;

    public CatalogEndpointsTests()
    {
        _outputRoot = Path.Combine(_tempRoot, "out");
        _booksRoot = Path.Combine(_tempRoot, "books");
        Directory.CreateDirectory(_outputRoot);
        Directory.CreateDirectory(_booksRoot);
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
    public async Task SetFolder_ScansEpubs_EditsMetadata_AndOpensInTextOnlyReader()
    {
        var sub = Path.Combine(_booksRoot, "SciFi");
        Directory.CreateDirectory(sub);
        CreateMinimalEpub(Path.Combine(sub, "Dune.epub"), "Dune", "Frank Herbert", "El desierto de Arrakis es inmenso.");

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var setResp = await client.PutAsJsonAsync("/api/library/folder", new SetLibraryFolderRequest(_booksRoot));
        Assert.Equal(HttpStatusCode.OK, setResp.StatusCode);
        var setPayload = await setResp.Content.ReadFromJsonAsync<SetLibraryFolderResponse>();
        Assert.NotNull(setPayload);
        Assert.Equal(_booksRoot, setPayload.BooksRoot);
        Assert.Equal(1, setPayload.Scan.Added);

        var catalog = await client.GetFromJsonAsync<LibraryCatalogResponse>("/api/library/catalog?q=dune");
        Assert.NotNull(catalog);
        var book = Assert.Single(catalog.Items);
        Assert.Equal("Dune", book.Title);
        Assert.Equal("Frank Herbert", book.Author);
        Assert.True(book.HasEpub);
        Assert.True(book.HasReader);
        Assert.Equal("none", book.AudioStatus);

        // Edit metadata
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/library/{book.Id}",
            new UpdateBookMetadataRequest(Series: "Crónicas de Dune", SeriesIndex: 1, Tags: ["Ciencia Ficción"]));
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patched = await patchResp.Content.ReadFromJsonAsync<CatalogBookDto>();
        Assert.NotNull(patched);
        Assert.Equal("Crónicas de Dune", patched.Series);
        Assert.Contains("Ciencia Ficción", patched.Tags);

        // Open directly in reader (lazy text-only sidecar generation!)
        var readerIndex = await client.GetFromJsonAsync<ReaderIndexResponse>($"/api/library/{book.Id}/reader");
        Assert.NotNull(readerIndex);
        Assert.True(readerIndex.Available);
        Assert.False(readerIndex.HasAudio);
        var chapter = Assert.Single(readerIndex.Chapters);

        var chapterJson = await client.GetStringAsync($"/api/library/{book.Id}/reader/chapters/{chapter.Index}");
        Assert.Contains("El desierto de Arrakis es inmenso.", chapterJson);
    }

    [Fact]
    public async Task ImportBooks_AddsToFolder_SkipsDuplicate_AndRejectsCorrupt()
    {
        var epubPath = Path.Combine(_tempRoot, "source.epub");
        CreateMinimalEpub(epubPath, "Fundación", "Isaac Asimov", "Trántor era la capital galáctica.");
        var validBytes = await File.ReadAllBytesAsync(epubPath);

        await using var factory = CreateFactory(_booksRoot);
        using var client = factory.CreateClient();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(validBytes), "files", "Fundacion.epub");
        form.Add(new ByteArrayContent(validBytes), "files", "Fundacion-dup.epub");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("bad")), "files", "roto.epub");

        var resp = await client.PostAsync("/api/library/import", form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var payload = await resp.Content.ReadFromJsonAsync<ImportBooksResponse>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload.Results.Count);
        Assert.Equal("imported", payload.Results[0].Status);
        Assert.Equal("skipped", payload.Results[1].Status);
        Assert.Equal("error", payload.Results[2].Status);
        Assert.Single(Directory.GetFiles(_booksRoot, "*.epub"));
    }

    [Fact]
    public async Task DeleteBook_RemovesFromCatalog_KeepsOriginalEpub_AndBlocksActiveJobWith409()
    {
        var epubPath = Path.Combine(_booksRoot, "Libro.epub");
        CreateMinimalEpub(epubPath, "Libro Uno", "Autor", "Texto de prueba.");

        await using var factory = CreateFactory(_booksRoot);
        using var client = factory.CreateClient();

        await client.PostAsync("/api/library/scan", null);
        var catalog = await client.GetFromJsonAsync<LibraryCatalogResponse>("/api/library/catalog");
        var book = Assert.Single(catalog!.Items);

        // Insert active job -> 409 Conflict
        var ctx = factory.Services.GetRequiredService<ILibraryContext>();
        using (var conn = ctx.OpenConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO conversion_jobs (id, book_id, status, options_json, created_at)
                VALUES ('job-active', @bookId, 'queued', '{}', '2026-01-01T00:00:00Z');
                """;
            cmd.Parameters.AddWithValue("@bookId", book.Id);
            cmd.ExecuteNonQuery();
        }

        var blocked = await client.DeleteAsync($"/api/library/{book.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        // Clear active job -> 204 NoContent, original EPUB stays untouched
        using (var conn = ctx.OpenConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM conversion_jobs WHERE id = 'job-active';";
            cmd.ExecuteNonQuery();
        }

        var deleted = await client.DeleteAsync($"/api/library/{book.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.True(File.Exists(epubPath));
    }

    private WebApplicationFactory<Program> CreateFactory(string booksRoot = "")
    {
        var settingsPath = Path.Combine(_tempRoot, $"settings-{Guid.NewGuid():N}.json");
        var store = new JsonSettingsStore(settingsPath);
        store.Save(new WebSettings(
            PiperModelDir: "",
            OutputRoot: _outputRoot,
            OllamaUrl: JsonSettingsStore.DefaultOllamaUrl,
            OllamaModel: JsonSettingsStore.DefaultOllamaModel,
            FfmpegOverride: "",
            BooksRoot: booksRoot));

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(store);
            });
        });
    }

    private static void CreateMinimalEpub(string path, string title, string author, string bodyText)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mime.Open(), new UTF8Encoding(false)))
        {
            w.Write("application/epub+zip");
        }

        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        WriteEntry(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="uid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="uid">urn:uuid:{Guid.NewGuid()}</dc:identifier>
                <dc:title>{title}</dc:title>
                <dc:creator>{author}</dc:creator>
                <dc:language>es</dc:language>
                <meta property="dcterms:modified">2026-01-01T00:00:00Z</meta>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/>
              </manifest>
              <spine><itemref idref="ch1"/></spine>
            </package>
            """);

        WriteEntry(zip, "OEBPS/nav.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <body><nav epub:type="toc"><ol><li><a href="ch1.xhtml">Capítulo 1</a></li></ol></nav></body>
            </html>
            """);

        WriteEntry(zip, "OEBPS/ch1.xhtml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
              <head><title>Capítulo 1</title></head>
              <body><h1>Capítulo 1</h1><p>{bodyText}</p></body>
            </html>
            """);
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }
}
