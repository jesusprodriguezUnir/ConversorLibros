namespace ConversorLibros.Core.Services;

/// <summary>
/// Builds ffmpeg argument lists. Pure functions: every token is one argument, so metadata values with quotes,
/// colons or apostrophes need no escaping at the process boundary.
/// </summary>
public static class FfmpegCommands
{
    public const string OpusBitrate = "16k";

    /// <summary>Encodes one chapter WAV to Ogg Opus (mono, VoIP mode) with per-file tags.</summary>
    public static IReadOnlyList<string> OpusChapter(
        string inputWav,
        string outputOgg,
        string title,
        string artist,
        string album,
        int track) =>
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-i", inputWav,
        "-vn",
        "-map_metadata", "-1",
        "-c:a", "libopus",
        "-b:a", OpusBitrate,
        "-application", "voip",
        "-ac", "1",
        "-metadata", $"title={title}",
        "-metadata", $"artist={artist}",
        "-metadata", $"album={album}",
        "-metadata", $"track={track}",
        "-f", "ogg",
        outputOgg
    ];

    /// <summary>
    /// Concatenates chapter WAVs (via a concat list), applies ffmetadata chapter markers and an optional cover
    /// into an AAC M4B. The cover is attached as a picture stream when <paramref name="coverPath"/> is set.
    /// </summary>
    public static IReadOnlyList<string> M4bPackage(
        string concatListPath,
        string metadataPath,
        string? coverPath,
        string outputM4b)
    {
        var args = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "concat", "-safe", "0", "-i", concatListPath,
            "-i", metadataPath
        };

        if (coverPath is not null)
        {
            args.AddRange(["-i", coverPath, "-map", "0:a", "-map", "2:v", "-c:v", "copy", "-disposition:v:0", "attached_pic"]);
        }
        else
        {
            args.AddRange(["-map", "0:a"]);
        }

        args.AddRange(["-map_metadata", "1", "-map_chapters", "1", "-c:a", "aac", "-b:a", "64k", "-ac", "1", outputM4b]);
        return args;
    }
}
