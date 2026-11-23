using ConversorLibros.Core.Services;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;

namespace ConversorLibros.Web.Endpoints;

public static class ReaderEndpoints
{
    private static readonly string[] CoverNames = ["cover.jpg", "cover.png"];

    public static IEndpointRouteBuilder MapReaderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library/{id}");

        group.MapGet("/reader", async (string id, LibraryBookResolver books, ReaderStore store, CancellationToken ct) =>
        {
            if (books.ResolveOpusBookDir(id) is not { } bookDir)
            {
                return NotFound();
            }

            var index = await store.ReadIndexAsync(bookDir, ct);
            if (index is null)
            {
                var reason = File.Exists(Path.Combine(bookDir, ReaderSidecarWriter.FolderName, "index.json")) ? "unreadable" : "no-data";
                return Results.Ok(new ReaderIndexResponse(false, reason, null, false, []));
            }

            var hasCover = CoverNames.Any(name => File.Exists(Path.Combine(bookDir, name)));
            return Results.Ok(new ReaderIndexResponse(true, null, index.Title, hasCover, index.Chapters ?? []));
        });

        group.MapGet("/reader/chapters/{n:int:min(0)}", async (
            string id, int n, LibraryBookResolver books, ReaderStore store, CancellationToken ct) =>
        {
            if (books.ResolveOpusBookDir(id) is not { } bookDir)
            {
                return NotFound();
            }

            var index = await store.ReadIndexAsync(bookDir, ct);
            if (index?.Chapters?.Any(c => c.Index == n) != true)
            {
                return NotFound();
            }

            try
            {
                var bytes = await store.ReadChapterBytesAsync(bookDir, n, ct);
                return bytes is null ? NotFound() : Results.Bytes(bytes, "application/json");
            }
            catch (InvalidDataException)
            {
                return Results.Json(new ErrorResponse("Los datos de lectura del capítulo están dañados."), statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });

        group.MapGet("/chapters/{n:int:min(0)}/audio", (string id, int n, LibraryBookResolver books) =>
        {
            if (books.ResolveOpusBookDir(id) is not { } bookDir)
            {
                return NotFound();
            }

            var file = Directory.EnumerateFiles(bookDir, AudiobookFileNames.ChapterPrefix(n) + "*.opus")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (file is null || !LibraryScanner.IsInside(books.OutputRoot, file))
            {
                return NotFound();
            }

            return Results.File(file, "audio/ogg", enableRangeProcessing: true);
        });

        group.MapGet("/reader/progress", async (string id, LibraryBookResolver books, ReaderStore store, CancellationToken ct) =>
        {
            if (books.ResolveOpusBookDir(id) is not { } bookDir)
            {
                return NotFound();
            }

            var progress = await store.GetProgressAsync(bookDir, ct);
            return progress is null ? Results.NoContent() : Results.Ok(progress);
        });

        group.MapPut("/reader/progress", async (
            string id, SaveProgressRequest request, LibraryBookResolver books, ReaderStore store, CancellationToken ct) =>
        {
            if (books.ResolveOpusBookDir(id) is not { } bookDir)
            {
                return NotFound();
            }

            var (status, progress) = await store.PutProgressAsync(bookDir, request.ChapterIndex, request.Ms, ct);
            return status == ReaderStoreStatus.Ok
                ? Results.Ok(progress)
                : Failure(status, "Posición de lectura no válida.");
        });

        group.MapGet("/reader/bookmarks", async (string id, LibraryBookResolver books, ReaderStore store, CancellationToken ct) =>
            books.ResolveOpusBookDir(id) is not { } bookDir
                ? NotFound()
                : Results.Ok(await store.ListBookmarksAsync(bookDir, ct)));

        group.MapPost("/reader/bookmarks", async (
            string id, CreateBookmarkRequest request, LibraryBookResolver books, ReaderStore store, CancellationToken ct) =>
        {
            if (books.ResolveOpusBookDir(id) is not { } bookDir)
            {
                return NotFound();
            }

            var (status, bookmark) = await store.AddBookmarkAsync(
                bookDir, request.ChapterIndex, request.Ms, request.Note, request.Excerpt, ct);
            return status == ReaderStoreStatus.Ok
                ? Results.Created($"/api/library/{id}/reader/bookmarks/{bookmark!.Id}", bookmark)
                : Failure(status, "Marcador no válido.");
        });

        group.MapPut("/reader/bookmarks/{bookmarkId:guid}", async (
            string id, Guid bookmarkId, UpdateBookmarkNoteRequest request,
            LibraryBookResolver books, ReaderStore store, CancellationToken ct) =>
        {
            if (books.ResolveOpusBookDir(id) is not { } bookDir)
            {
                return NotFound();
            }

            var (status, bookmark) = await store.UpdateBookmarkNoteAsync(bookDir, bookmarkId, request.Note, ct);
            return status == ReaderStoreStatus.Ok ? Results.Ok(bookmark) : Failure(status, "Nota no válida.");
        });

        group.MapDelete("/reader/bookmarks/{bookmarkId:guid}", async (
            string id, Guid bookmarkId, LibraryBookResolver books, ReaderStore store, CancellationToken ct) =>
        {
            if (books.ResolveOpusBookDir(id) is not { } bookDir)
            {
                return NotFound();
            }

            var status = await store.DeleteBookmarkAsync(bookDir, bookmarkId, ct);
            return status == ReaderStoreStatus.Ok ? Results.NoContent() : Failure(status, "Marcador no válido.");
        });

        return app;
    }

    private static IResult NotFound() => Results.NotFound(new ErrorResponse("Audiolibro no encontrado."));

    private static IResult Failure(ReaderStoreStatus status, string invalidMessage) => status switch
    {
        ReaderStoreStatus.NotFound => Results.NotFound(new ErrorResponse("Marcador no encontrado.")),
        ReaderStoreStatus.UnsupportedVersion => Results.Conflict(
            new ErrorResponse("El archivo de lectura es de una versión más reciente y no se modifica.")),
        _ => Results.BadRequest(new ErrorResponse(invalidMessage)),
    };
}
