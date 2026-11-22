using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Services;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;

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

        return AudiobookLibrary.Scan(outputRoot)
            .Select(summary => (ComputeId(summary.Path), summary))
            .ToList();
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
