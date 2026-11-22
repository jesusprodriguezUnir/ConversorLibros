using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ConversorLibros.Core.Models;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class ConversionEndpointsTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ConversorLibros.Web.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _uploadsRoot;

    public ConversionEndpointsTests()
    {
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
    public async Task StartConversion_UnknownUploadIdReturnsBadRequest()
    {
        await using var factory = CreateFactory(new FakeConversionRunner());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/conversions", new StartConversionRequest(
            UploadId: "missing",
            ChapterIndexes: [1],
            Speed: 1.0f,
            Translate: false,
            SourceLanguage: null,
            Format: "opus"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartConversion_EmptyChaptersReturnsBadRequest()
    {
        await using var factory = CreateFactory(new FakeConversionRunner());
        using var client = factory.CreateClient();
        var uploadId = await UploadMinimalBookAsync(client);

        var response = await client.PostAsJsonAsync("/api/conversions", new StartConversionRequest(
            UploadId: uploadId,
            ChapterIndexes: [],
            Speed: 1.0f,
            Translate: false,
            SourceLanguage: null,
            Format: "opus"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartConversion_WhileRunningReturnsConflict()
    {
        var gate = new TaskCompletionSource<ConversionRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeConversionRunner(async (_, _, _, ct) =>
        {
            started.TrySetResult();
            using var reg = ct.Register(() => gate.TrySetCanceled(ct));
            return await gate.Task;
        });

        await using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();
        var uploadId = await UploadMinimalBookAsync(client);

        var first = await client.PostAsJsonAsync("/api/conversions", new StartConversionRequest(
            uploadId, [1], 1.0f, false, null, "opus"));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await client.PostAsJsonAsync("/api/conversions", new StartConversionRequest(
            uploadId, [1], 1.0f, false, null, "opus"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        gate.TrySetResult(new ConversionRunResult(Path.Combine(_tempRoot, "out"), null));
    }

    [Fact]
    public async Task StartConversion_ReportsProgressAndCompletesAndPersistsLogsInCurrent()
    {
        var runner = new FakeConversionRunner((_, _, progress, _) =>
        {
            progress.Report(new ConversionProgress(1, 1, 1, 2, "Synthesizing"));
            progress.Report(new ConversionProgress(1, 1, 2, 2, "Assembled"));
            progress.Report(new ConversionProgress(0, 0, 0, 0, "Exporting"));
            return Task.FromResult(new ConversionRunResult(@"C:\out\Libro", null));
        });

        await using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();
        var uploadId = await UploadMinimalBookAsync(client);

        var startResp = await client.PostAsJsonAsync("/api/conversions", new StartConversionRequest(
            uploadId, [1], 1.0f, false, null, "opus"));
        Assert.Equal(HttpStatusCode.Accepted, startResp.StatusCode);

        ConversionStateDto? state = null;
        for (var i = 0; i < 40; i++)
        {
            state = await client.GetFromJsonAsync<ConversionStateDto>("/api/conversions/current");
            if (state?.Status == "completed")
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.NotNull(state);
        Assert.Equal("completed", state.Status);
        Assert.Equal("Completado", state.Stage);
        Assert.Equal(1.0, state.ProgressFraction, 2);
        Assert.Equal(@"C:\out\Libro", state.OutputPath);
        Assert.NotEmpty(state.Logs);
    }

    [Fact]
    public async Task CancelConversion_StopsActiveJobAndTransitionsToCancelled()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeConversionRunner(async (_, _, _, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new ConversionRunResult(null, null);
        });

        await using var factory = CreateFactory(runner);
        using var client = factory.CreateClient();
        var uploadId = await UploadMinimalBookAsync(client);

        var startResp = await client.PostAsJsonAsync("/api/conversions", new StartConversionRequest(
            uploadId, [1], 1.0f, false, null, "opus"));
        Assert.Equal(HttpStatusCode.Accepted, startResp.StatusCode);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cancelResp = await client.PostAsync("/api/conversions/cancel", null);
        Assert.Equal(HttpStatusCode.Accepted, cancelResp.StatusCode);

        ConversionStateDto? state = null;
        for (var i = 0; i < 40; i++)
        {
            state = await client.GetFromJsonAsync<ConversionStateDto>("/api/conversions/current");
            if (state?.Status == "cancelled")
            {
                break;
            }

            await Task.Delay(50);
        }

        Assert.NotNull(state);
        Assert.Equal("cancelled", state.Status);
        Assert.Equal("Cancelado", state.Stage);
    }

    [Fact]
    public async Task ConversionEvents_SseStreamsSnapshotAndProgressEvents()
    {
        await using var factory = CreateFactory(new FakeConversionRunner());
        using var client = factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/conversions/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        var eventLine = await reader.ReadLineAsync(cts.Token);
        var dataLine = await reader.ReadLineAsync(cts.Token);

        Assert.Equal("event: snapshot", eventLine);
        Assert.NotNull(dataLine);
        Assert.StartsWith("data: ", dataLine);
    }

    private WebApplicationFactory<Program> CreateFactory(IConversionRunner runner)
    {
        var settingsPath = Path.Combine(_tempRoot, "settings.json");
        var settingsStore = new JsonSettingsStore(settingsPath);

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<JsonSettingsStore>();
                services.RemoveAll<UploadedBookStore>();
                services.RemoveAll<IConversionRunner>();
                services.AddSingleton(settingsStore);
                services.AddSingleton(new UploadedBookStore(_uploadsRoot));
                services.AddSingleton(runner);
            });
        });
    }

    private static async Task<string> UploadMinimalBookAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(CreateMinimalEpubBytes());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/epub+zip");
        form.Add(content, "file", "book.epub");

        var response = await client.PostAsync("/api/books/inspect", form);
        response.EnsureSuccessStatusCode();
        var inspected = await response.Content.ReadFromJsonAsync<InspectedBookDto>();
        return inspected!.UploadId;
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
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:title>Libro Test</dc:title>
                    <dc:creator>Autor Test</dc:creator>
                    <dc:language>es</dc:language>
                    <dc:identifier id="bookid">urn:uuid:12345</dc:identifier>
                  </metadata>
                  <manifest>
                    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
                    <item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/>
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
                  <docTitle><text>Libro Test</text></docTitle>
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
                  <body><h1>Capítulo 1</h1><p>Texto de prueba.</p></body>
                </html>
                """);
        }

        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
        w.Write(content.Trim());
    }

    private sealed class FakeConversionRunner(
        Func<ConversionJobRequest, WebSettings, IProgress<ConversionProgress>, CancellationToken, Task<ConversionRunResult>>? handler = null)
        : IConversionRunner
    {
        public Task<ConversionRunResult> RunAsync(
            ConversionJobRequest request,
            WebSettings settings,
            IProgress<ConversionProgress> progress,
            CancellationToken ct) =>
            handler is not null
                ? handler(request, settings, progress, ct)
                : Task.FromResult(new ConversionRunResult(@"C:\out\Libro", null));
    }
}
