using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using ConversorLibros.Web.Services.Library;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class SqliteLibraryRepositoryTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "ConversorLibros.Web.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _outputRoot;
    private readonly LibraryContext _context;
    private readonly SqliteLibraryRepository _repository;

    public SqliteLibraryRepositoryTests()
    {
        _outputRoot = Path.Combine(_tempRoot, "out");
        Directory.CreateDirectory(_outputRoot);

        var settingsPath = Path.Combine(_tempRoot, "settings.json");
        var settingsStore = new JsonSettingsStore(settingsPath);
        settingsStore.Save(new WebSettings(
            PiperModelDir: "",
            OutputRoot: _outputRoot,
            OllamaUrl: JsonSettingsStore.DefaultOllamaUrl,
            OllamaModel: JsonSettingsStore.DefaultOllamaModel,
            FfmpegOverride: "",
            BooksRoot: Path.Combine(_tempRoot, "books")));

        _context = new LibraryContext(settingsStore);
        _repository = new SqliteLibraryRepository(_context);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task UpsertAndQuery_FiltersByAuthorSeriesTagAudioStatusAndSearchText()
    {
        var now = DateTimeOffset.UtcNow;

        await _repository.UpsertBookAsync(new BookRecord
        {
            Id = "book-1",
            Title = "El Imperio Final",
            SortTitle = "Imperio Final, El",
            Author = "Brandon Sanderson",
            Series = "Nacidos de la Bruma",
            SeriesIndex = 1,
            Description = "Una historia de alomancia y ceniza.",
            HasEpub = true,
            AudioStatus = "ready",
            AudioFormat = "opus",
            AudioDurationMs = 3600000,
            CreatedAt = now.AddMinutes(-30),
            LastReadAt = now.AddMinutes(-5),
        }, ["Fantasía", "Épica"]);

        await _repository.UpsertBookAsync(new BookRecord
        {
            Id = "book-2",
            Title = "El Pozo de la Ascensión",
            SortTitle = "Pozo de la Ascensión, El",
            Author = "Brandon Sanderson",
            Series = "Nacidos de la Bruma",
            SeriesIndex = 2,
            Description = "El asedio de Luthadel.",
            HasEpub = true,
            AudioStatus = "none",
            CreatedAt = now.AddMinutes(-20),
        }, ["Fantasía"]);

        await _repository.UpsertBookAsync(new BookRecord
        {
            Id = "book-3",
            Title = "Dune",
            SortTitle = "Dune",
            Author = "Frank Herbert",
            Series = "Dune",
            SeriesIndex = 1,
            Description = "Arrakis y la especia melange.",
            HasEpub = true,
            AudioStatus = "converting",
            CreatedAt = now.AddMinutes(-10),
        }, ["Ciencia Ficción"]);

        var allResult = await _repository.QueryAsync(new LibraryQuery(Sort: "created_at", Dir: "asc"));
        Assert.Equal(3, allResult.TotalCount);
        Assert.Equal(["book-1", "book-2", "book-3"], allResult.Items.Select(b => b.Id).ToArray());
        Assert.Equal(3, allResult.Facets.Counts.All);
        Assert.Equal(1, allResult.Facets.Counts.WithAudio);
        Assert.Equal(1, allResult.Facets.Counts.TextOnly);
        Assert.Equal(1, allResult.Facets.Counts.Converting);
        Assert.Contains(allResult.Facets.Authors, a => a.Name == "Brandon Sanderson" && a.Count == 2);
        Assert.Contains(allResult.Facets.Authors, a => a.Name == "Frank Herbert" && a.Count == 1);
        Assert.Contains(allResult.Facets.Series, s => s.Name == "Nacidos de la Bruma" && s.Count == 2);
        Assert.Contains(allResult.Facets.Tags, t => t.Name == "Fantasía" && t.Count == 2);

        var combined = await _repository.QueryAsync(new LibraryQuery(
            Query: "alomancia",
            Author: "Brandon Sanderson",
            Series: "Nacidos de la Bruma",
            Tag: "épica",
            AudioStatus: "ready",
            Sort: "title",
            Dir: "asc"));

        var single = Assert.Single(combined.Items);
        Assert.Equal("book-1", single.Id);
        Assert.Equal(1, combined.TotalCount);
        Assert.Contains("Épica", single.Tags);

        var sortedBySeriesDesc = await _repository.QueryAsync(new LibraryQuery(
            Author: "Brandon Sanderson",
            Sort: "series",
            Dir: "desc"));
        Assert.Equal(["book-2", "book-1"], sortedBySeriesDesc.Items.Select(b => b.Id).ToArray());
    }

    [Fact]
    public async Task FindBySha256AndEpubPath_DetectsExistingBooks()
    {
        var epubPath = Path.Combine(_tempRoot, "books", "Dune.epub");
        var sha = "abc123def456";

        await _repository.UpsertBookAsync(new BookRecord
        {
            Id = "book-sha",
            Title = "Dune",
            SortTitle = "Dune",
            EpubPath = epubPath,
            SourceSha256 = sha,
            HasEpub = true,
        }, []);

        var bySha = await _repository.FindBySha256Async(sha);
        Assert.NotNull(bySha);
        Assert.Equal("book-sha", bySha.Id);

        var byPath = await _repository.FindByEpubPathAsync(epubPath);
        Assert.NotNull(byPath);
        Assert.Equal("book-sha", byPath.Id);

        Assert.Null(await _repository.FindBySha256Async("nonexistent"));
        Assert.Null(await _repository.FindByEpubPathAsync(Path.Combine(_tempRoot, "books", "missing.epub")));
    }

    [Fact]
    public async Task UpdateMetadata_ReplacesTagsAndUpdatesFields()
    {
        await _repository.UpsertBookAsync(new BookRecord
        {
            Id = "book-meta",
            Title = "Original",
            SortTitle = "Original",
            Author = "Autor Viejo",
            Series = "Serie Vieja",
            SeriesIndex = 1,
            Description = "Descripción vieja",
        }, ["Viejo", "Común"]);

        var updated = await _repository.UpdateMetadataAsync("book-meta", new UpdateBookMetadataRequest(
            Title: "Nuevo Título",
            Author: "Autor Nuevo",
            Series: "Serie Nueva",
            SeriesIndex: 3.5,
            Publisher: "Nova",
            PublishedDate: "2024",
            Isbn: "978-84-000000-0",
            Description: "Descripción nueva",
            Language: "es",
            Tags: ["Nuevo", "Común"]));

        Assert.NotNull(updated);
        Assert.Equal("Nuevo Título", updated.Title);
        Assert.Equal("Autor Nuevo", updated.Author);
        Assert.Equal("Serie Nueva", updated.Series);
        Assert.Equal(3.5, updated.SeriesIndex);
        Assert.Equal("Nova", updated.Publisher);
        Assert.Equal("2024", updated.PublishedDate);
        Assert.Equal("978-84-000000-0", updated.Isbn);
        Assert.Equal("Descripción nueva", updated.Description);
        Assert.Equal("es", updated.Language);
        Assert.Equal(2, updated.Tags.Count);
        Assert.Contains("Nuevo", updated.Tags);
        Assert.Contains("Común", updated.Tags);
        Assert.DoesNotContain("Viejo", updated.Tags);

        var reloaded = await _repository.GetByIdAsync("book-meta");
        Assert.NotNull(reloaded);
        Assert.Equal("Nuevo Título", reloaded.Title);
        Assert.DoesNotContain("Viejo", reloaded.Tags);
    }

    [Fact]
    public async Task DeleteBook_RemovesBookAndCascadesTags()
    {
        await _repository.UpsertBookAsync(new BookRecord
        {
            Id = "book-del",
            Title = "Temporal",
            SortTitle = "Temporal",
        }, ["TagA", "TagB"]);

        var deleted = await _repository.DeleteBookAsync("book-del");
        Assert.True(deleted);
        Assert.Null(await _repository.GetByIdAsync("book-del"));

        using var conn = _context.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM book_tags WHERE book_id = @id";
        cmd.Parameters.AddWithValue("@id", "book-del");
        var remainingLinks = Convert.ToInt64(cmd.ExecuteScalar());
        Assert.Equal(0, remainingLinks);

        Assert.False(await _repository.DeleteBookAsync("book-del"));
    }
}
