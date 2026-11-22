using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;

namespace ConversorLibros.Web.Endpoints;

public static class BookEndpoints
{
    public static IEndpointRouteBuilder MapBookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/books");

        group.MapPost("/inspect", async (HttpRequest request, UploadedBookStore store, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new ErrorResponse("Envía el archivo EPUB como multipart/form-data."));
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ErrorResponse("No se recibió ningún archivo EPUB."));
            }

            if (!file.FileName.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ErrorResponse("Solo se admiten archivos con extensión .epub."));
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var entry = await store.StoreAndInspectAsync(stream, ct);
                var book = entry.Book;
                var hasCover = book.CoverImage is { Length: > 0 };

                var dto = new InspectedBookDto(
                    UploadId: entry.UploadId,
                    Title: book.Title,
                    Author: book.Author,
                    Language: string.IsNullOrWhiteSpace(book.Language) ? "en" : book.Language,
                    HasCover: hasCover,
                    CoverUrl: hasCover ? $"/api/books/{entry.UploadId}/cover" : null,
                    Chapters: book.Chapters
                        .Select(c => new ChapterSummaryDto(c.Index, c.Title, c.RawText.Length))
                        .ToList());

                return Results.Ok(dto);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new ErrorResponse($"No se pudo leer el EPUB: {ex.Message}"));
            }
        }).DisableAntiforgery();

        group.MapGet("/{uploadId}/cover", (string uploadId, UploadedBookStore store) =>
        {
            if (!store.TryGet(uploadId, out var entry) || entry?.Book.CoverImage is not { Length: > 0 })
            {
                return Results.NotFound();
            }

            var mime = string.IsNullOrWhiteSpace(entry.Book.CoverMimeType)
                ? "image/jpeg"
                : entry.Book.CoverMimeType;
            return Results.File(entry.Book.CoverImage, mime);
        });

        return app;
    }
}
