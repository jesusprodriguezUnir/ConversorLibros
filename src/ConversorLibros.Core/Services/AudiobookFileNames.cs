using System.Text;

namespace ConversorLibros.Core.Services;

/// <summary>Windows-safe names for book folders, chapter files and covers.</summary>
public static class AudiobookFileNames
{
    /// <summary>Upper bound for one path component; keeps folder + file names well under MAX_PATH.</summary>
    public const int MaxComponentLength = 120;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>
    /// Replaces characters Windows forbids with spaces, collapses whitespace, trims trailing dots and spaces,
    /// prefixes reserved device names and caps the length. Falls back to <paramref name="fallback"/> when empty.
    /// </summary>
    public static string SanitizeComponent(string? value, string fallback = "Audiolibro")
    {
        var builder = new StringBuilder(value?.Length ?? 0);
        foreach (var c in value ?? string.Empty)
        {
            builder.Append(char.IsControl(c) || Array.IndexOf(InvalidChars, c) >= 0 ? ' ' : c);
        }

        var collapsed = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var cleaned = collapsed.TrimEnd('.', ' ').TrimStart('.', ' ');

        if (cleaned.Length == 0)
        {
            return fallback;
        }

        var stem = cleaned.Split('.', 2)[0];
        if (ReservedNames.Contains(stem))
        {
            cleaned = "_" + cleaned;
        }

        return cleaned.Length > MaxComponentLength
            ? cleaned[..MaxComponentLength].TrimEnd('.', ' ')
            : cleaned;
    }

    /// <summary>Chapter file name that sorts by book chapter number: <c>001 - Title.opus</c>.</summary>
    public static string ChapterFileName(int chapterIndex, string? chapterTitle) =>
        $"{ChapterPrefix(chapterIndex)}{SanitizeComponent(chapterTitle, $"Capítulo {chapterIndex}")}.opus";

    /// <summary>Prefix shared by every file of one chapter, used to replace stale exports of that chapter.</summary>
    public static string ChapterPrefix(int chapterIndex) => $"{chapterIndex:D3} - ";
}
