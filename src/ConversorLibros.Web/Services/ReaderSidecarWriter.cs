using System.Text.Json;
using ConversorLibros.Core.Models;

namespace ConversorLibros.Web.Services;

/// <summary>
/// Writes the read-along sidecars under <c>&lt;BookDir&gt;/reader/</c>: <c>chapter_NNN.json</c> per chapter and an
/// <c>index.json</c> merged by chapter index. Every file is written to <c>&lt;file&gt;.partial</c> and then moved into
/// place, so readers never see a half-written <c>.json</c>. Progress and bookmarks are never touched here.
/// </summary>
public static class ReaderSidecarWriter
{
    public const string FolderName = "reader";
    public const int SchemaVersion = 1;

    /// <summary>Audio kind recorded per index entry. Reader data is only produced for Opus books.</summary>
    public const string OpusAudioKind = "opus";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static async Task WriteAsync(
        string bookDir,
        string bookTitle,
        IReadOnlyList<ReaderChapter> chapters,
        CancellationToken ct)
    {
        var readerDir = Path.Combine(bookDir, FolderName);
        Directory.CreateDirectory(readerDir);

        var indexPath = Path.Combine(readerDir, "index.json");
        var entries = await ReadIndexEntriesAsync(indexPath, ct).ConfigureAwait(false);

        // Chapter files first: the index must never list a chapter whose file is absent.
        foreach (var chapter in chapters)
        {
            var file = new ChapterFile(
                SchemaVersion,
                chapter.ChapterIndex,
                chapter.Title,
                chapter.DurationMs,
                chapter.Chunks.Select(c => new ChunkEntry(c.Order, c.StartMs, c.EndMs)).ToList(),
                chapter.Paragraphs);
            await WriteAtomicAsync(Path.Combine(readerDir, $"chapter_{chapter.ChapterIndex:D3}.json"), file, ct).ConfigureAwait(false);

            entries[chapter.ChapterIndex] = new IndexEntry(chapter.ChapterIndex, chapter.Title, chapter.DurationMs, OpusAudioKind);
        }

        var index = new IndexFile(SchemaVersion, bookTitle, entries.OrderBy(e => e.Key).Select(e => e.Value).ToList());
        await WriteAtomicAsync(indexPath, index, ct).ConfigureAwait(false);
    }

    /// <summary>A missing or malformed index starts fresh; an index from a newer schema is never overwritten.</summary>
    private static async Task<Dictionary<int, IndexEntry>> ReadIndexEntriesAsync(string indexPath, CancellationToken ct)
    {
        var entries = new Dictionary<int, IndexEntry>();
        if (!File.Exists(indexPath))
        {
            return entries;
        }

        IndexFile? existing;
        try
        {
            await using var stream = File.OpenRead(indexPath);
            existing = await JsonSerializer.DeserializeAsync<IndexFile>(stream, JsonOptions, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return entries;
        }

        if (existing is null)
        {
            return entries;
        }

        if (existing.SchemaVersion != SchemaVersion)
        {
            throw new InvalidDataException($"'{indexPath}' has unsupported schemaVersion {existing.SchemaVersion}.");
        }

        foreach (var entry in existing.Chapters ?? [])
        {
            entries[entry.Index] = entry with { AudioKind = entry.AudioKind ?? OpusAudioKind };
        }

        return entries;
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

    private sealed record IndexFile(int SchemaVersion, string Title, IReadOnlyList<IndexEntry>? Chapters);

    private sealed record ChunkEntry(int Order, long StartMs, long EndMs);

    private sealed record ChapterFile(
        int SchemaVersion,
        int ChapterIndex,
        string Title,
        long DurationMs,
        IReadOnlyList<ChunkEntry> Chunks,
        IReadOnlyList<ReaderParagraph> Paragraphs);
}
