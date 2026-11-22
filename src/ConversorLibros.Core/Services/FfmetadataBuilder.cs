using System.Text;

namespace ConversorLibros.Core.Services;

/// <summary>Builds an ffmetadata1 document with book tags and one chapter marker per audio file.</summary>
public static class FfmetadataBuilder
{
    public static string Build(string title, string author, IReadOnlyList<(string Title, TimeSpan Duration)> chapters)
    {
        // Explicit LF: AppendLine would emit CRLF on Windows, which the ffmetadata format does not require.
        var meta = new StringBuilder();
        meta.Append(";FFMETADATA1\n");
        meta.Append($"title={Escape(title)}\n");
        meta.Append($"artist={Escape(author)}\n");
        meta.Append($"album={Escape(title)}\n");
        meta.Append("genre=Audiobook\n");

        long startMs = 0;
        foreach (var (chapterTitle, duration) in chapters)
        {
            var endMs = startMs + (long)duration.TotalMilliseconds;
            meta.Append("[CHAPTER]\n");
            meta.Append("TIMEBASE=1/1000\n");
            meta.Append($"START={startMs}\n");
            meta.Append($"END={endMs}\n");
            meta.Append($"title={Escape(chapterTitle)}\n");
            startMs = endMs;
        }

        return meta.ToString();
    }

    /// <summary>Escapes the characters ffmetadata treats as syntax: backslash, '=', ';', '#' and newlines.</summary>
    public static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("=", "\\=").Replace(";", "\\;").Replace("#", "\\#").Replace("\n", "\\\n");
}
