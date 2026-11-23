using System.Text.Json;
using ConversorLibros.Core.Models;
using ConversorLibros.Web.Services;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class ReaderSidecarWriterTests : IDisposable
{
    private readonly string _bookDir = Path.Combine(Path.GetTempPath(), "ConversorLibros.Web.Tests", Guid.NewGuid().ToString("N"));
    private string ReaderDir => Path.Combine(_bookDir, "reader");

    public ReaderSidecarWriterTests() => Directory.CreateDirectory(_bookDir);

    [Fact]
    public async Task WriteAsync_WritesCamelCaseChapterAndIndexWithoutPartialFiles()
    {
        await ReaderSidecarWriter.WriteAsync(_bookDir, "El libro", [Chapter(3, "Tres", 812345)], CancellationToken.None);

        using var chapter = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(ReaderDir, "chapter_003.json")));
        var root = chapter.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(3, root.GetProperty("chapterIndex").GetInt32());
        Assert.Equal("Tres", root.GetProperty("title").GetString());
        Assert.Equal(812345, root.GetProperty("durationMs").GetInt64());
        var chunk = Assert.Single(root.GetProperty("chunks").EnumerateArray());
        Assert.Equal(0, chunk.GetProperty("order").GetInt32());
        Assert.Equal(0, chunk.GetProperty("startMs").GetInt64());
        Assert.Equal(812345, chunk.GetProperty("endMs").GetInt64());
        Assert.False(chunk.TryGetProperty("normalizedText", out _));
        Assert.Equal(0, root.GetProperty("paragraphs").GetArrayLength());

        using var index = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(ReaderDir, "index.json")));
        Assert.Equal(1, index.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("El libro", index.RootElement.GetProperty("title").GetString());
        var entry = Assert.Single(index.RootElement.GetProperty("chapters").EnumerateArray());
        Assert.Equal(3, entry.GetProperty("index").GetInt32());
        Assert.Equal("Tres", entry.GetProperty("title").GetString());
        Assert.Equal(812345, entry.GetProperty("durationMs").GetInt64());
        Assert.Equal("opus", entry.GetProperty("audioKind").GetString());

        Assert.Empty(Directory.EnumerateFiles(ReaderDir, "*.partial"));
    }

    [Fact]
    public async Task WriteAsync_ChapterSubset_MergesIndexAndLeavesExistingChaptersUntouched()
    {
        await ReaderSidecarWriter.WriteAsync(_bookDir, "Libro", [Chapter(1, "Uno", 1000), Chapter(2, "Dos", 2000), Chapter(3, "Tres", 3000)], CancellationToken.None);
        var oldStamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var n in new[] { 1, 2, 3 })
        {
            File.SetLastWriteTimeUtc(Path.Combine(ReaderDir, $"chapter_{n:D3}.json"), oldStamp);
        }

        await ReaderSidecarWriter.WriteAsync(_bookDir, "Libro", [Chapter(5, "Cinco", 5000), Chapter(4, "Cuatro", 4000)], CancellationToken.None);

        Assert.Equal([1, 2, 3, 4, 5], IndexedChapters().Select(c => c.Index));
        foreach (var n in new[] { 1, 2, 3 })
        {
            Assert.Equal(oldStamp, File.GetLastWriteTimeUtc(Path.Combine(ReaderDir, $"chapter_{n:D3}.json")));
        }

        Assert.True(File.Exists(Path.Combine(ReaderDir, "chapter_004.json")));
        Assert.True(File.Exists(Path.Combine(ReaderDir, "chapter_005.json")));
    }

    [Fact]
    public async Task WriteAsync_ReconvertedChapter_ReplacesFileAndIndexEntryWithoutDuplicates()
    {
        await ReaderSidecarWriter.WriteAsync(_bookDir, "Libro", [Chapter(1, "Uno", 1000), Chapter(2, "Dos", 2000)], CancellationToken.None);
        var otherStamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(ReaderDir, "chapter_001.json"), otherStamp);

        await ReaderSidecarWriter.WriteAsync(_bookDir, "Libro", [Chapter(2, "Dos bis", 2500)], CancellationToken.None);

        var chapters = IndexedChapters();
        Assert.Equal(2, chapters.Count);
        Assert.Equal(("Dos bis", 2500L), (chapters[1].Title, chapters[1].DurationMs));
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(ReaderDir, "chapter_002.json")));
        Assert.Equal(2500, doc.RootElement.GetProperty("durationMs").GetInt64());
        Assert.Equal(otherStamp, File.GetLastWriteTimeUtc(Path.Combine(ReaderDir, "chapter_001.json")));
    }

    [Fact]
    public async Task WriteAsync_FailureMidWrite_LeavesPreviousFileIntact()
    {
        await ReaderSidecarWriter.WriteAsync(_bookDir, "Libro", [Chapter(2, "Dos", 2000)], CancellationToken.None);
        var target = Path.Combine(ReaderDir, "chapter_002.json");
        var before = await File.ReadAllTextAsync(target);
        // A directory squatting on the .partial path makes the temp write fail.
        Directory.CreateDirectory(target + ".partial");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            ReaderSidecarWriter.WriteAsync(_bookDir, "Libro", [Chapter(2, "Nuevo", 9999)], CancellationToken.None));

        Assert.Equal(before, await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task WriteAsync_UnknownIndexSchemaVersion_IsNotOverwritten()
    {
        Directory.CreateDirectory(ReaderDir);
        var indexPath = Path.Combine(ReaderDir, "index.json");
        const string future = "{\"schemaVersion\":2,\"title\":\"x\",\"chapters\":[]}";
        await File.WriteAllTextAsync(indexPath, future);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ReaderSidecarWriter.WriteAsync(_bookDir, "Libro", [Chapter(1, "Uno", 1000)], CancellationToken.None));

        Assert.Equal(future, await File.ReadAllTextAsync(indexPath));
    }

    [Fact]
    public async Task WriteAsync_CorruptIndex_IsReplaced()
    {
        Directory.CreateDirectory(ReaderDir);
        await File.WriteAllTextAsync(Path.Combine(ReaderDir, "index.json"), "{ not json");

        await ReaderSidecarWriter.WriteAsync(_bookDir, "Libro", [Chapter(1, "Uno", 1000)], CancellationToken.None);

        Assert.Equal([1], IndexedChapters().Select(c => c.Index));
    }

    private List<(int Index, string Title, long DurationMs)> IndexedChapters()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ReaderDir, "index.json")));
        return doc.RootElement.GetProperty("chapters").EnumerateArray()
            .Select(e => (e.GetProperty("index").GetInt32(), e.GetProperty("title").GetString()!, e.GetProperty("durationMs").GetInt64()))
            .ToList();
    }

    private static ReaderChapter Chapter(int index, string title, long durationMs) =>
        new(index, title, durationMs, [new ChunkTiming(0, "texto interno", 0, durationMs)], []);

    public void Dispose()
    {
        if (Directory.Exists(_bookDir))
        {
            Directory.Delete(_bookDir, recursive: true);
        }
    }
}
