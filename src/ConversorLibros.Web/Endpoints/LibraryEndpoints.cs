using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Services;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace ConversorLibros.Web.Endpoints;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library");

        group.MapGet("/", (JsonSettingsStore settingsStore) =>
        {
            var outputRoot = settingsStore.Get().OutputRoot;
            var entries = ScanIndexed(outputRoot);
            var dtos = entries.Select(e => ToDto(e.Id, e.Summary)).ToList();
            return Results.Ok(dtos);
        });

        group.MapGet("/{id}/cover", (string id, JsonSettingsStore settingsStore) =>
        {
            var match = FindById(settingsStore.Get().OutputRoot, id);
            if (match?.CoverPath is null || !File.Exists(match.CoverPath))
            {
                return Results.NotFound();
            }

            var contentType = match.CoverPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";
            return Results.File(match.CoverPath, contentType);
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

        group.MapPost("/{id}/open", (string id, JsonSettingsStore settingsStore) =>
        {
            var match = FindById(settingsStore.Get().OutputRoot, id);
            if (match is null)
            {
                return Results.NotFound(new ErrorResponse("Audiolibro no encontrado."));
            }

            var startInfo = Directory.Exists(match.Path)
                ? new ProcessStartInfo("explorer.exe") { ArgumentList = { match.Path } }
                : new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + match.Path } };

            Process.Start(startInfo);
            return Results.NoContent();
        });

        return app;
    }

    private static IReadOnlyList<(string Id, AudiobookSummary Summary)> ScanIndexed(string outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot) || !Directory.Exists(outputRoot))
        {
            return [];
        }

        return LibraryScanner.Scan(outputRoot)
            .Select(summary => (ComputeId(summary.Path), summary))
            .ToList();
    }

    /// <summary>
    /// Streams the book's Opus chapters and cover as a zip straight to the response body. Entries are stored
    /// (opus is already compressed) and the archive is disposed before the handler completes.
    /// </summary>
    private static async Task WriteZipAsync(string bookFolder, Stream body, HttpContext http, CancellationToken ct)
    {
        // ZipArchive writes synchronously even in its async APIs (the central directory and entry disposal),
        // and Kestrel/TestServer reject sync IO by default. Opt in for this request only; nothing is buffered.
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
            CoverUrl: hasCover ? $"/api/library/{id}/cover" : null);
    }

    private static string ComputeId(string fullPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToLowerInvariant()));
        return Convert.ToBase64String(hash)[..16]
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
