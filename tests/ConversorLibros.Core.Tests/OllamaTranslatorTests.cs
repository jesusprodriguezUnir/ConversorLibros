using System.Net;
using System.Text;
using System.Text.Json;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class OllamaTranslatorTests
{
    private const string BaseUrl = "http://localhost:11434";

    [Fact]
    public async Task TranslateAsync_SendsExpectedGenerateRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"response":"Hola"}""");
        var sut = CreateSut(handler);

        await sut.TranslateAsync("Hello world", "en", "es");

        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal($"{BaseUrl}/api/generate", handler.Request.RequestUri!.ToString());

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var root = body.RootElement;
        Assert.Equal("qwen3:8b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("think").GetBoolean());
        Assert.Contains("Hello world", root.GetProperty("prompt").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("system").GetString()));
        Assert.Equal(0, root.GetProperty("options").GetProperty("temperature").GetDouble());
        Assert.Equal(42, root.GetProperty("options").GetProperty("seed").GetInt32());
        Assert.Equal(8192, root.GetProperty("options").GetProperty("num_ctx").GetInt32());
    }

    [Fact]
    public async Task TranslateAsync_TrimsResponse()
    {
        var sut = CreateSut(new RecordingHandler(HttpStatusCode.OK, """{"response":"  Hola  \n"}"""));

        var result = await sut.TranslateAsync("Hello", "en", "es");

        Assert.Equal("Hola", result);
    }

    [Fact]
    public async Task TranslateAsync_EmptyResponse_Throws()
    {
        var sut = CreateSut(new RecordingHandler(HttpStatusCode.OK, """{"response":"   "}"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.TranslateAsync("Hello", "en", "es"));
    }

    [Fact]
    public async Task TranslateAsync_ThinkingOutput_Throws()
    {
        var sut = CreateSut(new RecordingHandler(HttpStatusCode.OK, """{"response":"<think>hmm</think>Hola"}"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.TranslateAsync("Hello", "en", "es"));
    }

    [Fact]
    public async Task TranslateAsync_NonSuccessStatus_Throws()
    {
        var sut = CreateSut(new RecordingHandler(HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.TranslateAsync("Hello", "en", "es"));
    }

    [Fact]
    public void ModelVersion_IncludesModelAndPromptVersion()
    {
        var sut = CreateSut(new RecordingHandler(HttpStatusCode.OK, """{"response":"x"}"""));

        Assert.Equal("qwen3:8b|v1", sut.ModelVersion);
    }

    private static OllamaTranslator CreateSut(HttpMessageHandler handler) =>
        new(new HttpClient(handler), BaseUrl, "qwen3:8b");

    private sealed class RecordingHandler(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
