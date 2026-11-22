using ConversorLibros.Core.Abstractions;

namespace ConversorLibros.Core.Services;

/// <summary>A finished audiobook found in the output root.</summary>
/// <param name="Title">Album tag, falling back to the folder or file name.</param>
/// <param name="Author">Artist tag of the first chapter, or null when unknown.</param>
/// <param name="Duration">Sum of chapter durations (Opus only; null when not cheap to know).</param>
/// <param name="CoverPath">cover.jpg/cover.png inside the book folder, if present.</param>
public sealed record AudiobookSummary(
    string Title,
    string? Author,
    AudiobookFormat Format,
    string Path,
    int ChapterCount,
    TimeSpan? Duration,
    string? CoverPath);

/// <summary>
/// Lists finished audiobooks: folders that contain Opus chapters and loose <c>.m4b</c> files.
/// Work folders (cache, chapter WAVs) are ignored because they hold no <c>.opus</c> files.
/// </summary>
public static class AudiobookLibrary
{
    private static readonly string[] CoverNames = ["cover.jpg", "cover.png"];

    public static IReadOnlyList<AudiobookSummary> Scan(string outputRoot)
    {
        if (!Directory.Exists(outputRoot))
        {
            return [];
        }

        var books = new List<AudiobookSummary>();

        foreach (var folder in Directory.EnumerateDirectories(outputRoot))
        {
            var chapters = Directory.EnumerateFiles(folder, "*.opus").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
            if (chapters.Count == 0)
            {
                continue;
            }

            var infos = chapters.Select(OggOpusInfo.Read).ToList();
            var first = infos[0].Tags;
            var cover = CoverNames.Select(name => System.IO.Path.Combine(folder, name)).FirstOrDefault(File.Exists);

            TimeSpan? total = infos.All(i => i.Duration is not null)
                ? infos.Aggregate(TimeSpan.Zero, (sum, i) => sum + i.Duration!.Value)
                : null;

            books.Add(new AudiobookSummary(
                Title: first.TryGetValue("ALBUM", out var album) && !string.IsNullOrWhiteSpace(album)
                    ? album
                    : System.IO.Path.GetFileName(folder),
                Author: first.TryGetValue("ARTIST", out var artist) && !string.IsNullOrWhiteSpace(artist) ? artist : null,
                Format: AudiobookFormat.Opus,
                Path: folder,
                ChapterCount: chapters.Count,
                Duration: total,
                CoverPath: cover));
        }

        foreach (var file in Directory.EnumerateFiles(outputRoot, "*.m4b"))
        {
            books.Add(new AudiobookSummary(
                Title: System.IO.Path.GetFileNameWithoutExtension(file),
                Author: null,
                Format: AudiobookFormat.M4b,
                Path: file,
                ChapterCount: 0,
                Duration: null,
                CoverPath: null));
        }

        return books.OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
