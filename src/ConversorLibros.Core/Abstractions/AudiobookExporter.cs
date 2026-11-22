using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Abstractions;

public enum AudiobookFormat
{
    /// <summary>One folder per book with an Ogg Opus file per chapter, plus cover art.</summary>
    Opus,

    /// <summary>Single AAC M4B file with chapter markers.</summary>
    M4b
}

/// <summary>A chapter audio file produced by the pipeline, with the title shown to the listener.</summary>
/// <param name="Index">1-based book chapter number; drives file ordering.</param>
/// <param name="Title">Title as shown to the listener (translated when translation is enabled).</param>
/// <param name="AudioPath">Chapter WAV file.</param>
public sealed record AudiobookChapter(int Index, string Title, string AudioPath);

/// <summary>Packages chapter audio into a listenable audiobook.</summary>
public interface IAudiobookExporter
{
    AudiobookFormat Format { get; }

    /// <returns>Path of the produced folder (Opus) or file (M4B).</returns>
    Task<string> ExportAsync(
        Book book,
        IReadOnlyList<AudiobookChapter> chapters,
        string outputRoot,
        CancellationToken cancellationToken = default);
}
