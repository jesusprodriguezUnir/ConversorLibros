using System.Text.Json;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;

namespace ConversorLibros.Web.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings");

        group.MapGet("/", (JsonSettingsStore store) => Results.Ok(store.GetResponse()));

        group.MapPut("/", (WebSettings settings, JsonSettingsStore store) =>
        {
            var saved = store.Save(settings);
            return Results.Ok(store.BuildResponse(saved));
        });

        group.MapPost("/test-ollama", async (TestOllamaRequest request, CancellationToken ct) =>
        {
            var url = string.IsNullOrWhiteSpace(request.Url)
                ? JsonSettingsStore.DefaultOllamaUrl
                : request.Url.Trim();
            var model = (request.Model ?? string.Empty).Trim();

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var response = await http.GetAsync($"{url.TrimEnd('/')}/api/tags", ct);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                var models = new List<string>();
                if (document.RootElement.TryGetProperty("models", out var modelsElement) &&
                    modelsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in modelsElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("name", out var nameEl) && nameEl.GetString() is { } name)
                        {
                            models.Add(name);
                        }
                    }
                }

                var installed = !string.IsNullOrWhiteSpace(model) && models.Contains(model);
                var message = installed
                    ? $"Conectado. El modelo '{model}' está disponible."
                    : string.IsNullOrWhiteSpace(model)
                        ? $"Conectado. {models.Count} modelo(s) disponible(s)."
                        : $"Conectado, pero el modelo '{model}' no está instalado.";

                return Results.Ok(new TestOllamaResponse(true, installed, models, message));
            }
            catch (TaskCanceledException)
            {
                return Results.Ok(new TestOllamaResponse(
                    false,
                    false,
                    [],
                    "Tiempo de espera agotado (10 s)."));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or UriFormatException)
            {
                return Results.Ok(new TestOllamaResponse(
                    false,
                    false,
                    [],
                    $"No se pudo conectar: {ex.Message}"));
            }
        });

        return app;
    }
}
