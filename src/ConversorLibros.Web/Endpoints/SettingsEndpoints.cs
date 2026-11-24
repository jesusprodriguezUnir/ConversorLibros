using System.Text.Json;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using ConversorLibros.Web.Services.Library;

namespace ConversorLibros.Web.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings");

        group.MapGet("/", (JsonSettingsStore store) => Results.Ok(store.GetResponse()));

        group.MapPut("/", async (
            WebSettings settings,
            JsonSettingsStore store,
            SqliteLibraryRepository repository,
            ILibraryContext libraryContext,
            ConversionJobManager jobManager,
            CancellationToken ct) =>
        {
            var current = store.Get();
            var incomingOutputRoot = string.IsNullOrWhiteSpace(settings.OutputRoot)
                ? JsonSettingsStore.DefaultOutputRoot
                : settings.OutputRoot.Trim();

            var outputRootChanged = !string.Equals(
                current.OutputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                incomingOutputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

            if (outputRootChanged)
            {
                var runningInMemory = string.Equals(jobManager.GetSnapshot().Status, "running", StringComparison.OrdinalIgnoreCase);
                if (runningInMemory || await repository.HasActiveOrQueuedJobsAsync(ct))
                {
                    return Results.Conflict(new ErrorResponse(
                        "No se puede cambiar la carpeta de biblioteca mientras hay conversiones en curso o en cola."));
                }
            }

            var saved = store.Save(settings);
            if (outputRootChanged)
            {
                libraryContext.EnsureInitialized();
            }

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
