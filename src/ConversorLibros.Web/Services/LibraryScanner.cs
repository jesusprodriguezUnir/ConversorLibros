using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Services;

namespace ConversorLibros.Web.Services;

/// <summary>
/// Web-host library listing. Core's scan covers the legacy layouts (<c>&lt;root&gt;/&lt;Title&gt;/*.opus</c> and
/// <c>&lt;root&gt;/&lt;Title&gt;.m4b</c>); this adds the per-book layout <c>&lt;root&gt;/&lt;Title&gt;/&lt;Title&gt;.m4b</c>.
/// Only direct children are inspected, so <c>work/</c> is never listed.
/// </summary>
public static class LibraryScanner
{
    private static readonly string[] CoverNames = ["cover.jpg", "cover.png"];

    public static IReadOnlyList<AudiobookSummary> Scan(string outputRoot)
    {
        if (!Directory.Exists(outputRoot))
        {
            return [];
        }

        var books = AudiobookLibrary.Scan(outputRoot).ToList();

        foreach (var folder in Directory.EnumerateDirectories(outputRoot))
        {
            if (Directory.EnumerateFiles(folder, "*.opus").Any())
            {
                continue;
            }

            var cover = FindCover(folder);
            foreach (var file in Directory.EnumerateFiles(folder, "*.m4b").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                books.Add(new AudiobookSummary(
                    Title: Path.GetFileNameWithoutExtension(file),
                    Author: null,
                    Format: AudiobookFormat.M4b,
                    Path: file,
                    ChapterCount: 0,
                    Duration: null,
                    CoverPath: cover));
            }
        }

        return books.OrderBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Top-level .opus chapters and cover of an Opus book folder; work files are never included.</summary>
    public static IReadOnlyList<string> SelectZipFiles(string bookFolder) =>
        Directory.EnumerateFiles(bookFolder)
            .Where(f => f.EndsWith(".opus", StringComparison.OrdinalIgnoreCase)
                        || CoverNames.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// True when <paramref name="candidate"/> is strictly below <paramref name="root"/> once both are fully resolved,
    /// and no existing component below <paramref name="root"/> is a junction or symlink (which could point anywhere).
    /// </summary>
    public static bool IsInside(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        if (fullCandidate.Length <= fullRoot.Length
            || !fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var current = fullRoot;
        foreach (var segment in fullCandidate[fullRoot.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Path.Exists(current))
            {
                break;
            }

            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }
        }

        return true;
    }

    private static string? FindCover(string folder) =>
        CoverNames.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
}
