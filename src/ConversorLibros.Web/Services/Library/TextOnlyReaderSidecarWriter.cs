using System.Text.Json;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;

namespace ConversorLibros.Web.Services.Library;

/// <summary>
/// Writes text-only reader sidecars under <c>&lt;BookCacheDir&gt;/reader/</c> so any EPUB in the library
/// can be opened and read immediately without running TTS first. Uses the exact same on-disk schema as
/// <see cref="ReaderSidecarWriter"/>, with <c>audioKind = null</c> and <c>durationMs = 0</c>.
/// </summary>
public sealed class TextOnlyReaderSidecarWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly SpanishTextNormalizer Normalizer = new();

    public static async Task<string?> WriteCoverAsync(Book book, string targetBookCacheDir, CancellationToken ct = default)
    {
        if (book.CoverImage is not { Length: > 0 })
        {
            return null;
        }

        Directory.CreateDirectory(targetBookCacheDir);
        var ext = string.Equals(book.CoverMimeType, "image/png", StringComparison.OrdinalIgnoreCase)
            ? ".png"
            : ".jpg";
        var coverPath = Path.Combine(targetBookCacheDir, "cover" + ext);
        var partial = coverPath + ".partial";
        await File.WriteAllBytesAsync(partial, book.CoverImage, ct).ConfigureAwait(false);
        File.Move(partial, coverPath, overwrite: true);
        return coverPath;
    }

    public async Task WriteAsync(Book book, string targetBookCacheDir, CancellationToken ct = default)
    {
        var readerDir = Path.Combine(targetBookCacheDir, ReaderSidecarWriter.FolderName);
        Directory.CreateDirectory(readerDir);

        var entries = new List<IndexEntry>(book.Chapters.Count);
        foreach (var chapter in book.Chapters)
        {
            var paragraphs = BuildParagraphs(chapter.RawText);
            var file = new ChapterFile(
                ReaderSidecarWriter.SchemaVersion,
                chapter.Index,
                chapter.Title,
                0,
                [],
                paragraphs);
            await WriteAtomicAsync(
                Path.Combine(readerDir, $"chapter_{chapter.Index:D3}.json"),
                file,
                ct).ConfigureAwait(false);

            entries.Add(new IndexEntry(chapter.Index, chapter.Title, 0, null));
        }

        var index = new IndexFile(ReaderSidecarWriter.SchemaVersion, book.Title, entries);
        await WriteAtomicAsync(Path.Combine(readerDir, "index.json"), index, ct).ConfigureAwait(false);
        await WriteCoverAsync(book, targetBookCacheDir, ct).ConfigureAwait(false);
    }

    public async Task<bool> EnsureGeneratedAsync(BookRecord book, string targetBookCacheDir, CancellationToken ct = default)
    {
        var indexPath = Path.Combine(targetBookCacheDir, ReaderSidecarWriter.FolderName, "index.json");
        if (File.Exists(indexPath))
        {
            return true;
        }

        if (!book.HasEpub || string.IsNullOrWhiteSpace(book.EpubPath) || !File.Exists(book.EpubPath))
        {
            return false;
        }

        var source = new EpubBookSource();
        var loaded = await source.LoadAsync(book.EpubPath, ct).ConfigureAwait(false);
        await WriteAsync(loaded, targetBookCacheDir, ct).ConfigureAwait(false);
        return true;
    }

    private static IReadOnlyList<ReaderParagraph> BuildParagraphs(string rawText)
    {
        var rawParagraphs = ReaderTextSegmenter.SplitParagraphs(rawText);
        var result = new List<ReaderParagraph>(rawParagraphs.Count);
        foreach (var rawParagraph in rawParagraphs)
        {
            var segments = ReaderTextSegmenter.SplitSentences(rawParagraph, s => Normalizer.Normalize(s));
            var sentences = segments.Count > 0
                ? segments.Select(s => new ReaderSentence(s.Raw, 0, 0)).ToList()
                : [new ReaderSentence(rawParagraph, 0, 0)];
            result.Add(new ReaderParagraph("sentence", sentences));
        }

        return result;
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken ct)
    {
        var partial = path + ".partial";
        await using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(partial, path, overwrite: true);
    }

    private sealed record IndexEntry(int Index, string Title, long DurationMs, string? AudioKind);
    private sealed record IndexFile(int SchemaVersion, string Title, IReadOnlyList<IndexEntry> Chapters);
    private sealed record ChunkEntry(int Order, long StartMs, long EndMs);
    private sealed record ChapterFile(
        int SchemaVersion,
        int ChapterIndex,
        string Title,
        long DurationMs,
        IReadOnlyList<ChunkEntry> Chunks,
        IReadOnlyList<ReaderParagraph> Paragraphs);
}
