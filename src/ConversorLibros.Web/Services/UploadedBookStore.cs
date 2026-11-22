using System.Collections.Concurrent;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;

namespace ConversorLibros.Web.Services;

public sealed record UploadedBook(string UploadId, string EpubPath, Book Book, DateTimeOffset UploadedAt);

/// <summary>
/// Manages temporarily uploaded EPUB files and their parsed metadata for web conversions.
/// </summary>
public sealed class UploadedBookStore
{
    private const int MaxRetainedUploads = 5;

    private readonly string _uploadsRoot;
    private readonly ConcurrentDictionary<string, UploadedBook> _books = new();
    private readonly object _cleanupGate = new();

    public UploadedBookStore(string? uploadsRoot = null)
    {
        _uploadsRoot = uploadsRoot ?? Path.Combine(Path.GetTempPath(), "ConversorLibros", "uploads");
    }

    public async Task<UploadedBook> StoreAndInspectAsync(Stream fileStream, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_uploadsRoot);
        var uploadId = Guid.NewGuid().ToString("N");
        var tempPath = Path.Combine(_uploadsRoot, $"{uploadId}.epub");

        try
        {
            await using (var fs = File.Create(tempPath))
            {
                await fileStream.CopyToAsync(fs, ct);
            }

            var book = await new EpubBookSource().LoadAsync(tempPath, ct);
            var entry = new UploadedBook(uploadId, tempPath, book, DateTimeOffset.UtcNow);
            _books[uploadId] = entry;
            TrimOldEntries();
            return entry;
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    public bool TryGet(string uploadId, out UploadedBook? entry) =>
        _books.TryGetValue(uploadId, out entry);

    private void TrimOldEntries()
    {
        lock (_cleanupGate)
        {
            if (_books.Count <= MaxRetainedUploads)
            {
                return;
            }

            var toRemove = _books.Values
                .OrderByDescending(b => b.UploadedAt)
                .Skip(MaxRetainedUploads)
                .ToList();

            foreach (var old in toRemove)
            {
                if (_books.TryRemove(old.UploadId, out _))
                {
                    TryDeleteFile(old.EpubPath);
                }
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Ignore locked temp files during cleanup.
        }
    }
}
