using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class LibraryAndBookEndpointsTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ConversorLibros.Web.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _outputRoot;
    private readonly string _uploadsRoot;

    public LibraryAndBookEndpointsTests()
    {
        _outputRoot = Path.Combine(_tempRoot, "library");
        _uploadsRoot = Path.Combine(_tempRoot, "uploads");
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
    public async Task GetLibrary_NonExistentOutputRootReturnsEmptyList()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/library");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<LibraryItemDto>>();
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetLibrary_ScansM4bAndServesStableIds()
    {
        Directory.CreateDirectory(_outputRoot);
        File.WriteAllBytes(Path.Combine(_outputRoot, "El Quijote.m4b"), [0x00, 0x01, 0x02]);

        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var first = await client.GetFromJsonAsync<List<LibraryItemDto>>("/api/library");
        var second = await client.GetFromJsonAsync<List<LibraryItemDto>>("/api/library");

        Assert.NotNull(first);
        Assert.NotNull(second);
        var book = Assert.Single(first);
        Assert.Equal("El Quijote", book.Title);
        Assert.Equal("m4b", book.Format);
        Assert.Equal(book.Id, second[0].Id);
    }

    [Fact]
    public async Task GetLibraryCover_UnknownIdReturnsNotFound()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/library/does-not-exist/cover");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InspectBook_InvalidNonEpubFileReturnsBadRequestAndCleansTempFile()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("not a real epub"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/epub+zip");
        form.Add(content, "file", "fake.epub");

        var response = await client.PostAsync("/api/books/inspect", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error.Error));
        if (Directory.Exists(_uploadsRoot))
        {
            Assert.Empty(Directory.GetFiles(_uploadsRoot));
        }
    }

    [Fact]
    public async Task InspectBook_ValidEpubReturnsMetadataChaptersAndCoverEndpoint()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var epubBytes = CreateMinimalEpubBytes();
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(epubBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/epub+zip");
        form.Add(content, "file", "prueba.epub");

        var response = await client.PostAsync("/api/books/inspect", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var inspected = await response.Content.ReadFromJsonAsync<InspectedBookDto>();
        Assert.NotNull(inspected);
        Assert.False(string.IsNullOrWhiteSpace(inspected.UploadId));
        Assert.Equal("Libro de Prueba", inspected.Title);
        Assert.Equal("Autor Ejemplo", inspected.Author);
        Assert.Equal("en", inspected.Language);
        var chapter = Assert.Single(inspected.Chapters);
        Assert.Equal(1, chapter.Index);
        Assert.True(chapter.CharCount > 0);
        Assert.True(inspected.HasCover);
        Assert.NotNull(inspected.CoverUrl);

        var coverResponse = await client.GetAsync(inspected.CoverUrl);
        Assert.Equal(HttpStatusCode.OK, coverResponse.StatusCode);
    }

    [Fact]
    public async Task GetUploadedBookCover_UnknownUploadIdReturnsNotFound()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/books/unknown-id/cover");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
                services.AddSingleton(new UploadedBookStore(_uploadsRoot));
            });
        });
    }

    private static byte[] CreateMinimalEpubBytes()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var w = new StreamWriter(mime.Open(), Encoding.ASCII))
                w.Write("application/epub+zip");

            WriteEntry(zip, "META-INF/container.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                  </rootfiles>
                </container>
                """);

            WriteEntry(zip, "OEBPS/content.opf", """
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="bookid">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
                    <dc:title>Libro de Prueba</dc:title>
                    <dc:creator>Autor Ejemplo</dc:creator>
                    <dc:language>en</dc:language>
                    <dc:identifier id="bookid">urn:uuid:12345</dc:identifier>
                    <meta name="cover" content="cover-img"/>
                  </metadata>
                  <manifest>
                    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
                    <item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/>
                    <item id="cover-img" href="cover.png" media-type="image/png"/>
                  </manifest>
                  <spine toc="ncx">
                    <itemref idref="ch1"/>
                  </spine>
                </package>
                """);

            WriteEntry(zip, "OEBPS/toc.ncx", """
                <?xml version="1.0" encoding="UTF-8"?>
                <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
                  <head><meta name="dtb:uid" content="urn:uuid:12345"/></head>
                  <docTitle><text>Libro de Prueba</text></docTitle>
                  <navMap>
                    <navPoint id="np1" playOrder="1">
                      <navLabel><text>Capítulo 1</text></navLabel>
                      <content src="ch1.xhtml"/>
                    </navPoint>
                  </navMap>
                </ncx>
                """);

            WriteEntry(zip, "OEBPS/ch1.xhtml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <head><title>Capítulo 1</title></head>
                  <body><h1>Capítulo 1</h1><p>Este es un párrafo de prueba para el libro.</p></body>
                </html>
                """);

            // Valid 1x1 PNG
            var pngBytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
            var coverEntry = zip.CreateEntry("OEBPS/cover.png");
            using var coverStream = coverEntry.Open();
            coverStream.Write(pngBytes);
        }

        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
        w.Write(content.Trim());
    }
}
