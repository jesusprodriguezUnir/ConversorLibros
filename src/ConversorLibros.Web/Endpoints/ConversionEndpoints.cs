using System.Text.Json;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;

namespace ConversorLibros.Web.Endpoints;

public static class ConversionEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IEndpointRouteBuilder MapConversionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/conversions");

        group.MapGet("/current", (ConversionJobManager manager) => Results.Ok(manager.GetSnapshot()));

        group.MapPost("/", (StartConversionRequest request, UploadedBookStore uploads, ConversionJobManager manager) =>
        {
            if (string.IsNullOrWhiteSpace(request.UploadId) || !uploads.TryGet(request.UploadId, out var uploaded) || uploaded is null)
            {
                return Results.BadRequest(new ErrorResponse("El libro seleccionado ya no está disponible. Vuelve a cargar el EPUB."));
            }

            if (request.ChapterIndexes is null || request.ChapterIndexes.Count == 0)
            {
                return Results.BadRequest(new ErrorResponse("Selecciona al menos un capítulo para convertir."));
            }

            if (!manager.TryStart(uploaded, request, out var snapshot))
            {
                return Results.Conflict(new ErrorResponse("Ya hay una conversión en curso."));
            }

            return Results.Accepted("/api/conversions/current", snapshot);
        });

        group.MapPost("/cancel", (ConversionJobManager manager) =>
        {
            manager.Cancel();
            return Results.Accepted("/api/conversions/current", manager.GetSnapshot());
        });

        group.MapGet("/events", async (HttpContext context, ConversionJobManager manager, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream";

            var (id, reader) = manager.Subscribe();
            try
            {
                await WriteEventAsync(context.Response, "snapshot", manager.GetSnapshot(), ct);

                await foreach (var evt in reader.ReadAllAsync(ct))
                {
                    await WriteEventAsync(context.Response, evt.EventName, evt.State, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected.
            }
            finally
            {
                manager.Unsubscribe(id);
            }
        });

        return app;
    }

    private static async Task WriteEventAsync(HttpResponse response, string eventName, ConversionStateDto state, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
