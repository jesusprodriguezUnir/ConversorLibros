using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ConversorLibros.Core.Abstractions;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Translates text through a local Ollama server (<c>/api/generate</c>, non-streaming).
/// </summary>
public sealed class OllamaTranslator : ITranslator
{
    /// <summary>Bump when the system prompt changes so cached translations are invalidated.</summary>
    public const string PromptVersion = "v1";

    private const string DefaultModel = "qwen3:8b";

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _model;

    public OllamaTranslator(HttpClient httpClient, string baseUrl, string model = DefaultModel)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Ollama base URL cannot be empty.", nameof(baseUrl));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Ollama model cannot be empty.", nameof(model));
        }

        _httpClient = httpClient;
        _endpoint = baseUrl.TrimEnd('/') + "/api/generate";
        _model = model;
    }

    public string ModelVersion => $"{_model}|{PromptVersion}";

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
    {
        var request = new GenerateRequest(
            Model: _model,
            Stream: false,
            Think: false,
            System: BuildSystemPrompt(sourceLanguage, targetLanguage),
            Prompt: text,
            Options: new GenerateOptions(Temperature: 0, Seed: 42, NumCtx: 8192));

        using var response = await _httpClient.PostAsJsonAsync(_endpoint, request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Ollama returned HTTP {(int)response.StatusCode} for model '{_model}'.");
        }

        var body = await response.Content.ReadFromJsonAsync<GenerateResponse>(cancellationToken).ConfigureAwait(false);
        var output = body?.Response;
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Ollama returned an empty translation.");
        }

        if (output.Contains("<think>", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ollama returned reasoning output (<think>) instead of a translation.");
        }

        return output.Trim();
    }

    private static string BuildSystemPrompt(string sourceLanguage, string targetLanguage)
    {
        var target = string.Equals(targetLanguage, "es", StringComparison.OrdinalIgnoreCase)
            ? "Spanish (Spain)"
            : targetLanguage;

        return $"""
            You are a professional literary translator. Translate the user's text from {sourceLanguage} to {target}.
            Output only the translation. Preserve paragraph breaks. Do not add notes, comments, headings or explanations.
            """;
    }

    private sealed record GenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("think")] bool Think,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("options")] GenerateOptions Options);

    private sealed record GenerateOptions(
        [property: JsonPropertyName("temperature")] float Temperature,
        [property: JsonPropertyName("seed")] int Seed,
        [property: JsonPropertyName("num_ctx")] int NumCtx);

    private sealed record GenerateResponse([property: JsonPropertyName("response")] string? Response);
}
