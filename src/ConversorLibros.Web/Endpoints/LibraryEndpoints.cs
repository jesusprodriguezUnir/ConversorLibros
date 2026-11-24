using System.Diagnostics;
using System.IO.Compression;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Services;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using ConversorLibros.Web.Services.Library;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace ConversorLibros.Web.Endpoints;

public static class LibraryEndpoints
{
    private static readonly string[] CoverNames = ["cover.jpg", "cover.png"];

    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library");

        // Legacy flat audiobook listing (preserved for backward compatibility).
        group.MapGet("/", (JsonSettingsStore settingsStore) =>
        {
            var outputRoot = settingsStore.Get().OutputRoot;
            var entries = ScanIndexed(outputRoot);
            var dtos = entries.Select(e => ToDto(e.Id, e.Summary)).ToList();
            return Results.Ok(dtos);
        });

        // Rich Calibre-style catalog backed by SQLite + in-place BooksRoot folder.
        group.MapGet("/catalog", async (
            string? q,
            string? author,
            string? series,
            string? tag,
            string? audioStatus,
            string? sort,
            string? dir,
            SqliteLibraryRepository repository,
            LegacyLibraryImporter legacyImporter,
            ILibraryContext context,
            CancellationToken ct) =>
        {
            await legacyImporter.SyncAsync(ct);

            var result = await repository.QueryAsync(
                new LibraryQuery(q, author, series, tag, audioStatus, sort, dir),
                ct);

            var items = result.Items.Select(b => ToCatalogDto(b, context)).ToList();
            return Results.Ok(new LibraryCatalogResponse(
                items,
                result.TotalCount,
                result.Facets,
                context.BooksRoot,
                context.OutputRoot));
        });

        group.MapPut("/folder", async (
            SetLibraryFolderRequest request,
            JsonSettingsStore settingsStore,
            LibraryFolderScanner scanner,
            LegacyLibraryImporter legacyImporter,
            CancellationToken ct) =>
        {
            var path = (request.Path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return Results.BadRequest(new ErrorResponse("La carpeta seleccionada no existe."));
            }

            var current = settingsStore.Get();
            var saved = settingsStore.Save(current with { BooksRoot = path });

            await legacyImporter.SyncAsync(ct);
            var summary = await scanner.ScanAsync(ct);
            return Results.Ok(new SetLibraryFolderResponse(saved.BooksRoot, summary));
        });

        group.MapPost("/scan", async (
            LibraryFolderScanner scanner,
            LegacyLibraryImporter legacyImporter,
            CancellationToken ct) =>
        {
            await legacyImporter.SyncAsync(ct);
            var summary = await scanner.ScanAsync(ct);
            return Results.Ok(summary);
        });

        group.MapPost("/import", async (
            HttpRequest httpRequest,
            BookImportService importService,
            CancellationToken ct) =>
        {
            if (!httpRequest.HasFormContentType)
            {
                return Results.BadRequest(new ErrorResponse("Se esperaba multipart/form-data con archivos .epub."));
            }

            var form = await httpRequest.ReadFormAsync(ct);
            if (form.Files.Count == 0)
            {
                return Results.BadRequest(new ErrorResponse("No se recibieron archivos para importar."));
            }

            var streams = new List<(string FileName, Stream Content)>(form.Files.Count);
            try
            {
                foreach (var file in form.Files)
                {
                    streams.Add((file.FileName, file.OpenReadStream()));
                }

                var results = await importService.AddToFolderAsync(streams, ct);
                return Results.Ok(new ImportBooksResponse(results));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
            finally
            {
                foreach (var (_, stream) in streams)
                {
                    await stream.DisposeAsync();
                }
            }
        }).DisableAntiforgery();

        group.MapGet("/{id}", async (
            string id,
            SqliteLibraryRepository repository,
            ILibraryContext context,
            CancellationToken ct) =>
        {
            var book = await repository.GetByIdAsync(id, ct);
            return book is null
                ? Results.NotFound(new ErrorResponse("Libro no encontrado."))
                : Results.Ok(ToCatalogDto(book, context));
        });

        group.MapPatch("/{id}", async (
            string id,
            UpdateBookMetadataRequest patch,
            SqliteLibraryRepository repository,
            ILibraryContext context,
            CancellationToken ct) =>
        {
            var updated = await repository.UpdateMetadataAsync(id, patch, ct);
            return updated is null
                ? Results.NotFound(new ErrorResponse("Libro no encontrado."))
                : Results.Ok(ToCatalogDto(updated, context));
        });

        group.MapPut("/{id}/cover", async (
            string id,
            HttpRequest httpRequest,
            SqliteLibraryRepository repository,
            ILibraryContext context,
            CancellationToken ct) =>
        {
            var book = await repository.GetByIdAsync(id, ct);
            if (book is null)
            {
                return Results.NotFound(new ErrorResponse("Libro no encontrado."));
            }

            if (!httpRequest.HasFormContentType)
            {
                return Results.BadRequest(new ErrorResponse("Se esperaba una imagen en multipart/form-data."));
            }

            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new ErrorResponse("No se recibió ninguna imagen de portada."));
            }

            var isPng = string.Equals(file.ContentType, "image/png", StringComparison.OrdinalIgnoreCase)
                        || file.FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            var cacheDir = context.GetBookCacheDir(id);
            Directory.CreateDirectory(cacheDir);

            foreach (var oldName in CoverNames)
            {
                var oldPath = Path.Combine(cacheDir, oldName);
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
            }

            var targetPath = Path.Combine(cacheDir, isPng ? "cover.png" : "cover.jpg");
            var partial = targetPath + ".partial";
            await using (var source = file.OpenReadStream())
            await using (var dest = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await source.CopyToAsync(dest, ct);
            }

            File.Move(partial, targetPath, overwrite: true);
            await repository.UpdateCoverFlagAsync(id, true, ct);
            return Results.NoContent();
        }).DisableAntiforgery();

        group.MapDelete("/{id}", async (
            string id,
            SqliteLibraryRepository repository,
            ILibraryContext context,
            CancellationToken ct) =>
        {
            var book = await repository.GetByIdAsync(id, ct);
            if (book is null)
            {
                return Results.NotFound(new ErrorResponse("Libro no encontrado."));
            }

            if (await repository.HasActiveOrQueuedJobForBookAsync(id, ct))
            {
                return Results.Conflict(new ErrorResponse("No se puede eliminar un libro mientras tiene una conversión activa o en cola."));
            }

            await repository.DeleteBookAsync(id, ct);

            // Clean up internal cache directory only; never delete user's original EPUB in BooksRoot or legacy files.
            var cacheRoot = Path.Combine(context.OutputRoot, LibraryContext.CacheFolderName);
            var bookCacheDir = context.GetBookCacheDir(id);
            if (Directory.Exists(bookCacheDir) && LibraryScanner.IsInside(cacheRoot, bookCacheDir))
            {
                Directory.Delete(bookCacheDir, recursive: true);
            }

            return Results.NoContent();
        });

        group.MapGet("/{id}/cover", async (
            string id,
            JsonSettingsStore settingsStore,
            SqliteLibraryRepository repository,
            ILibraryContext context,
            CancellationToken ct) =>
        {
            var match = FindById(settingsStore.Get().OutputRoot, id);
            var coverPath = match?.CoverPath is not null && File.Exists(match.CoverPath)
                ? match.CoverPath
                : await ResolveCatalogCoverPathAsync(id, repository, context, ct);

            if (coverPath is null || !File.Exists(coverPath))
            {
                return Results.NotFound();
            }

            var contentType = coverPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";
            return Results.File(coverPath, contentType);
        });

        group.MapGet("/{id}/download", (string id, JsonSettingsStore settingsStore, HttpContext http) =>
        {
            var outputRoot = settingsStore.Get().OutputRoot;
            var match = FindById(outputRoot, id);
            if (match is null || !LibraryScanner.IsInside(outputRoot, match.Path))
            {
                return Results.NotFound(new ErrorResponse("Audiolibro no encontrado."));
            }

            if (match.Format == AudiobookFormat.M4b)
            {
                SetAttachmentHeader(http, Path.GetFileName(match.Path));
                return Results.File(match.Path, "audio/mp4", enableRangeProcessing: true);
            }

            SetAttachmentHeader(http, AudiobookFileNames.SanitizeComponent(match.Title) + ".zip");
            var bookFolder = match.Path;
            return Results.Stream(
                body => WriteZipAsync(bookFolder, body, http, http.RequestAborted),
                contentType: "application/zip");
        });

        group.MapPost("/{id}/open", async (
            string id,
            JsonSettingsStore settingsStore,
            SqliteLibraryRepository repository,
            ILibraryContext context,
            CancellationToken ct) =>
        {
            string? targetPath = FindById(settingsStore.Get().OutputRoot, id)?.Path;
            if (targetPath is null)
            {
                var book = await repository.GetByIdAsync(id, ct);
                if (book is not null)
                {
                    if (!string.IsNullOrWhiteSpace(book.EpubPath) && File.Exists(book.EpubPath))
                    {
                        targetPath = book.EpubPath;
                    }
                    else if (!string.IsNullOrWhiteSpace(book.FolderPath))
                    {
                        var candidate = Path.IsPathRooted(book.FolderPath)
                            ? book.FolderPath
                            : Path.Combine(context.OutputRoot, book.FolderPath);
                        if (Path.Exists(candidate))
                        {
                            targetPath = candidate;
                        }
                    }
                }
            }

            if (targetPath is null)
            {
                return Results.NotFound(new ErrorResponse("Libro no encontrado."));
            }

            var startInfo = Directory.Exists(targetPath)
                ? new ProcessStartInfo("explorer.exe") { ArgumentList = { targetPath } }
                : new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + targetPath } };

            Process.Start(startInfo);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<string?> ResolveCatalogCoverPathAsync(
        string id,
        SqliteLibraryRepository repository,
        ILibraryContext context,
        CancellationToken ct)
    {
        var book = await repository.GetByIdAsync(id, ct);
        if (book is null)
        {
            return null;
        }

        var cacheDir = context.GetBookCacheDir(id);
        foreach (var name in CoverNames)
        {
            var candidate = Path.Combine(cacheDir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(book.FolderPath))
        {
            var folder = Path.IsPathRooted(book.FolderPath)
                ? book.FolderPath
                : Path.Combine(context.OutputRoot, book.FolderPath);
            var dir = Directory.Exists(folder) ? folder : Path.GetDirectoryName(folder);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                foreach (var name in CoverNames)
                {
                    var candidate = Path.Combine(dir, name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private static CatalogBookDto ToCatalogDto(BookRecord b, ILibraryContext context)
    {
        var hasReader = b.HasEpub;
        if (!hasReader && !string.IsNullOrWhiteSpace(b.FolderPath) && string.Equals(b.AudioFormat, "opus", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.IsPathRooted(b.FolderPath)
                ? b.FolderPath
                : Path.Combine(context.OutputRoot, b.FolderPath);
            hasReader = File.Exists(Path.Combine(dir, ReaderSidecarWriter.FolderName, "index.json"));
        }

        return new CatalogBookDto(
            Id: b.Id,
            Title: b.Title,
            Author: string.IsNullOrWhiteSpace(b.Author) ? "Autor desconocido" : b.Author,
            Series: b.Series,
            SeriesIndex: b.SeriesIndex,
            Publisher: b.Publisher,
            PublishedDate: b.PublishedDate,
            Isbn: b.Isbn,
            Description: b.Description,
            Language: b.Language,
            Tags: b.Tags,
            EpubPath: b.EpubPath,
            HasEpub: b.HasEpub,
            HasCover: b.HasCover,
            CoverUrl: b.HasCover ? $"/api/library/{b.Id}/cover" : null,
            HasReader: hasReader,
            AudioStatus: b.AudioStatus,
            AudioFormat: b.AudioFormat,
            AudioDurationSeconds: b.AudioDurationMs.HasValue ? b.AudioDurationMs.Value / 1000.0 : null,
            ChapterCount: b.ChapterCount,
            ReadingChapter: b.ReadingChapter,
            ReadingPercent: b.ReadingPercent,
            LastReadAt: b.LastReadAt,
            IsLegacy: b.IsLegacy,
            CreatedAt: b.CreatedAt);
    }

    private static IReadOnlyList<(string Id, AudiobookSummary Summary)> ScanIndexed(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot))
        {
            return [];
        }

        var cacheDir = Path.GetFullPath(Path.Combine(outputRoot, LibraryContext.CacheFolderName));
        return LibraryScanner.Scan(outputRoot)
            .Where(s => !Path.GetFullPath(s.Path).StartsWith(cacheDir, StringComparison.OrdinalIgnoreCase))
            .Select(summary => (LibraryIds.Compute(summary.Path), summary))
            .ToList();
    }

    private static async Task WriteZipAsync(string bookFolder, Stream body, HttpContext http, CancellationToken ct)
    {
        http.Features.Get<IHttpBodyControlFeature>()!.AllowSynchronousIO = true;

        using var zip = new ZipArchive(body, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in LibraryScanner.SelectZipFiles(bookFolder))
        {
            var entry = zip.CreateEntry(Path.GetFileName(file), CompressionLevel.NoCompression);
            using var source = File.OpenRead(file);
            using var target = entry.Open();
            await source.CopyToAsync(target, ct);
        }
    }

    private static void SetAttachmentHeader(HttpContext http, string fileName)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(fileName);
        http.Response.Headers.ContentDisposition = disposition.ToString();
    }

    private static AudiobookSummary? FindById(string outputRoot, string id) =>
        ScanIndexed(outputRoot).FirstOrDefault(e => e.Id == id).Summary;

    private static LibraryItemDto ToDto(string id, AudiobookSummary summary)
    {
        var format = summary.Format == AudiobookFormat.M4b ? "m4b" : "opus";
        var hasCover = summary.CoverPath is not null && File.Exists(summary.CoverPath);
        return new LibraryItemDto(
            Id: id,
            Title: summary.Title,
            Author: summary.Author ?? "Autor desconocido",
            Format: format,
            ChapterCount: summary.ChapterCount,
            DurationSeconds: summary.Duration?.TotalSeconds,
            HasCover: hasCover,
            CoverUrl: hasCover ? $"/api/library/{id}/cover" : null,
            HasReader: summary.Format == AudiobookFormat.Opus
                       && File.Exists(Path.Combine(summary.Path, ReaderSidecarWriter.FolderName, "index.json")));
    }
}
