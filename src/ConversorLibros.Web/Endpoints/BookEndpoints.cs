using ConversorLibros.Core.Services;
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

                var chapters = book.Chapters.Select(BookDtoMapper.ToSummary).ToList();

                var dto = new InspectedBookDto(
                    UploadId: entry.UploadId,
                    Title: book.Title,
                    Author: book.Author,
                    Language: string.IsNullOrWhiteSpace(book.Language) ? "en" : book.Language,
                    HasCover: hasCover,
                    CoverUrl: hasCover ? $"/api/books/{entry.UploadId}/cover" : null,
                    Chapters: chapters,
                    FolderName: AudiobookFileNames.SanitizeComponent(book.Title),
                    Metadata: BookDtoMapper.ToMetadata(book.Metadata),
                    TotalWordCount: chapters.Sum(c => c.WordCount),
                    TotalEstimatedSeconds: chapters.Sum(c => c.EstimatedSeconds));

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

        group.MapGet("/{uploadId}/chapters/{index:int}", (string uploadId, int index, UploadedBookStore store) =>
        {
            if (!store.TryGet(uploadId, out var entry) || entry is null)
            {
                return Results.NotFound();
            }

            // Look up by the chapter's own Index: indexes are not contiguous once front matter is dropped.
            var chapter = entry.Book.Chapters.FirstOrDefault(c => c.Index == index);
            return chapter is null
                ? Results.NotFound()
                : Results.Ok(BookDtoMapper.ToPreview(chapter));
        });

        return app;
    }
}
