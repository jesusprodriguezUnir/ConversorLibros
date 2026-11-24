using System.IO.Compression;
using System.Text;
using ConversorLibros.Web.Models;
using ConversorLibros.Web.Services;
using ConversorLibros.Web.Services.Library;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class LibraryFolderScannerAndImportTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "ConversorLibros.Web.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _outputRoot;
    private readonly string _booksRoot;
    private readonly LibraryContext _context;
    private readonly SqliteLibraryRepository _repository;

    public LibraryFolderScannerAndImportTests()
    {
        _outputRoot = Path.Combine(_tempRoot, "out");
        _booksRoot = Path.Combine(_tempRoot, "my-books");
        Directory.CreateDirectory(_outputRoot);
        Directory.CreateDirectory(_booksRoot);

        var settingsStore = new JsonSettingsStore(Path.Combine(_tempRoot, "settings.json"));
        settingsStore.Save(new WebSettings(
            PiperModelDir: "",
            OutputRoot: _outputRoot,
            OllamaUrl: JsonSettingsStore.DefaultOllamaUrl,
            OllamaModel: JsonSettingsStore.DefaultOllamaModel,
            FfmpegOverride: "",
            BooksRoot: _booksRoot));

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
    public async Task ScanAsync_RecursivelyIndexesEpubsWithoutCopyingAndSkipsUnchangedOnRescan()
    {
        var nestedDir = Path.Combine(_booksRoot, "Sanderson", "Mistborn");
        Directory.CreateDirectory(nestedDir);

        var epub1 = Path.Combine(_booksRoot, "Dune.epub");
        var epub2 = Path.Combine(nestedDir, "Imperio.epub");
        var corrupt = Path.Combine(nestedDir, "roto.epub");

        CreateMinimalEpub(epub1, "Dune", "Frank Herbert", "Arrakis es un planeta desértico. La especia fluye.");
        CreateMinimalEpub(epub2, "El Imperio Final", "Brandon Sanderson", "Ceniza caía del cielo. Kelsier sonrió.");
        await File.WriteAllTextAsync(corrupt, "not a valid zip");

        var scanner = new LibraryFolderScanner(_context, _repository);
        var first = await scanner.ScanAsync();

        Assert.Equal(2, first.Added);
        Assert.Equal(0, first.Updated);
        Assert.Equal(0, first.Unchanged);
        Assert.Single(first.Errors);

        var all = await _repository.ListAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, b => b.Title == "Dune" && b.EpubPath == epub1 && b.HasEpub && !b.IsLegacy);
        Assert.Contains(all, b => b.Title == "El Imperio Final" && b.EpubPath == epub2 && b.HasEpub);

        // No EPUB copies created under OutputRoot/.library-cache
        Assert.Empty(Directory.EnumerateFiles(_outputRoot, "*.epub", SearchOption.AllDirectories));

        // Second scan skips unchanged files
        var second = await scanner.ScanAsync();
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(2, second.Unchanged);
        Assert.Single(second.Errors);
    }

    [Fact]
    public async Task ScanAsync_RemovesDeletedEpubWithNoAudio()
    {
        var epub1 = Path.Combine(_booksRoot, "Borrar.epub");
        CreateMinimalEpub(epub1, "Libro Temporal", "Autor", "Un capítulo breve.");

        var scanner = new LibraryFolderScanner(_context, _repository);
        await scanner.ScanAsync();
        Assert.Single(await _repository.ListAllAsync());

        File.Delete(epub1);
        var rescan = await scanner.ScanAsync();

        Assert.Equal(1, rescan.Removed);
        Assert.Empty(await _repository.ListAllAsync());
    }

    [Fact]
    public async Task AddToFolderAsync_HandlesValidDuplicateAndCorruptInSameBatch()
    {
        var tempEpub = Path.Combine(_tempRoot, "upload.epub");
        CreateMinimalEpub(tempEpub, "Fundación", "Isaac Asimov", "Hari Seldon predijo la caída del Imperio.");
        var validBytes = await File.ReadAllBytesAsync(tempEpub);
        var corruptBytes = Encoding.UTF8.GetBytes("corrupt epub payload");

        var importer = new BookImportService(_context, _repository);
        var results = await importer.AddToFolderAsync([
            ("Fundacion.epub", new MemoryStream(validBytes)),
            ("Fundacion-copia.epub", new MemoryStream(validBytes)),
            ("danado.epub", new MemoryStream(corruptBytes)),
        ]);

        Assert.Equal(3, results.Count);
        Assert.Equal("imported", results[0].Status);
        Assert.NotNull(results[0].BookId);
        Assert.Equal("Fundación", results[0].Title);

        Assert.Equal("skipped", results[1].Status);
        Assert.Equal("duplicate", results[1].Reason);
        Assert.Equal(results[0].BookId, results[1].BookId);

        Assert.Equal("error", results[2].Status);

        // Only the single valid EPUB exists in BooksRoot; zero corrupt or duplicate files
        var filesInBooksRoot = Directory.GetFiles(_booksRoot, "*.epub");
        var savedFile = Assert.Single(filesInBooksRoot);
        Assert.Equal("Fundacion.epub", Path.GetFileName(savedFile));
    }

    [Fact]
    public async Task LegacyLibraryImporter_RegistersExistingOpusAndM4bIdempotently()
    {
        var opusDir = Path.Combine(_outputRoot, "El Quijote");
        Directory.CreateDirectory(opusDir);
        await File.WriteAllBytesAsync(Path.Combine(opusDir, "001 - Cap 1.opus"), [1, 2, 3]);
        var m4bPath = Path.Combine(_outputRoot, "Dune.m4b");
        await File.WriteAllBytesAsync(m4bPath, [4, 5, 6]);

        var legacy = new LegacyLibraryImporter(_context, _repository);
        var first = await legacy.SyncAsync();
        var second = await legacy.SyncAsync();

        Assert.Equal(2, first);
        Assert.Equal(0, second);

        var books = await _repository.ListAllAsync();
        Assert.Equal(2, books.Count);
        Assert.All(books, b =>
        {
            Assert.True(b.IsLegacy);
            Assert.False(b.HasEpub);
            Assert.Equal("ready", b.AudioStatus);
        });
        Assert.True(File.Exists(m4bPath));
    }

    [Fact]
    public async Task TextOnlyReaderSidecarWriter_GeneratesReadableIndexAndChapter()
    {
        var epubPath = Path.Combine(_booksRoot, "Lectura.epub");
        CreateMinimalEpub(epubPath, "Lectura Inmediata", "Autor", "Primera frase del libro. Segunda frase para leer.");

        var scanner = new LibraryFolderScanner(_context, _repository);
        await scanner.ScanAsync();
        var book = Assert.Single(await _repository.ListAllAsync());

        var writer = new TextOnlyReaderSidecarWriter();
        var cacheDir = _context.GetBookCacheDir(book.Id);
        var generated = await writer.EnsureGeneratedAsync(book, cacheDir);

        Assert.True(generated);
        var store = new ReaderStore(NullLogger<ReaderStore>.Instance);
        var index = await store.ReadIndexAsync(cacheDir, CancellationToken.None);
        Assert.NotNull(index);
        Assert.Equal("Lectura Inmediata", index.Title);
        var ch = Assert.Single(index.Chapters!);
        Assert.Null(ch.AudioKind);
        Assert.Equal(0, ch.DurationMs);

        var bytes = await store.ReadChapterBytesAsync(cacheDir, ch.Index, CancellationToken.None);
        Assert.NotNull(bytes);
        var json = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Primera frase del libro.", json);
    }

    private static void CreateMinimalEpub(string path, string title, string author, string bodyText)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mime.Open(), new UTF8Encoding(false)))
        {
            w.Write("application/epub+zip");
        }

        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        WriteEntry(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="uid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="uid">urn:uuid:{Guid.NewGuid()}</dc:identifier>
                <dc:title>{title}</dc:title>
                <dc:creator>{author}</dc:creator>
                <dc:language>es</dc:language>
                <meta property="dcterms:modified">2026-01-01T00:00:00Z</meta>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/>
              </manifest>
              <spine>
                <itemref idref="ch1"/>
              </spine>
            </package>
            """);

        WriteEntry(zip, "OEBPS/nav.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
              <body>
                <nav epub:type="toc"><ol><li><a href="ch1.xhtml">Capítulo 1</a></li></ol></nav>
              </body>
            </html>
            """);

        WriteEntry(zip, "OEBPS/ch1.xhtml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
              <head><title>Capítulo 1</title></head>
              <body><h1>Capítulo 1</h1><p>{bodyText}</p></body>
            </html>
            """);
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }
}
