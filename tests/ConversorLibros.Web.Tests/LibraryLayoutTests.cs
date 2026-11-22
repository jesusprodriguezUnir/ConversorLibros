using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Services;
using ConversorLibros.Web.Services;
using Xunit;

namespace ConversorLibros.Web.Tests;

public sealed class LibraryLayoutTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ConversorLibros.Web.Tests", Guid.NewGuid().ToString("N"));

    public LibraryLayoutTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void BookOutputLayout_FinalAudioInBookFolderAndWorkFilesUnderWork()
    {
        var root = Path.Combine(_tempRoot, "out");

        var layout = BookOutputLayout.For(root, "El Quijote: Parte 1/2");

        var folderName = AudiobookFileNames.SanitizeComponent("El Quijote: Parte 1/2");
        Assert.Equal(Path.Combine(root, folderName), layout.BookDir);
        Assert.DoesNotContain(':', folderName);
        Assert.Equal(Path.Combine(layout.BookDir, "work"), layout.WorkDir);
        Assert.Equal(Path.Combine(layout.WorkDir, "cache"), layout.CacheDir);
        Assert.Equal(Path.Combine(layout.WorkDir, "translation-cache"), layout.TranslationCacheDir);
    }

    [Fact]
    public void Scan_ListsNestedM4bAndIgnoresWorkFolders()
    {
        var root = Path.Combine(_tempRoot, "out");
        var bookDir = Path.Combine(root, "Libro");
        Directory.CreateDirectory(Path.Combine(bookDir, "work"));
        File.WriteAllBytes(Path.Combine(bookDir, "Libro.m4b"), [0x00]);
        File.WriteAllBytes(Path.Combine(bookDir, "work", "chapter_001.wav"), [0x00]);
        File.WriteAllBytes(Path.Combine(bookDir, "work", "stray.opus"), [0x00]);

        var books = LibraryScanner.Scan(root);

        var book = Assert.Single(books);
        Assert.Equal("Libro", book.Title);
        Assert.Equal(AudiobookFormat.M4b, book.Format);
        Assert.Equal(Path.Combine(bookDir, "Libro.m4b"), book.Path);
    }

    [Fact]
    public void IsInside_RejectsTraversalSiblingPrefixAndRootItself()
    {
        var root = Path.Combine(_tempRoot, "out");

        Assert.True(LibraryScanner.IsInside(root, Path.Combine(root, "Libro", "Libro.m4b")));
        Assert.False(LibraryScanner.IsInside(root, Path.Combine(root, "..", "escape.m4b")));
        Assert.False(LibraryScanner.IsInside(root, Path.Combine(root, "Libro", "..", "..", "escape.m4b")));
        Assert.False(LibraryScanner.IsInside(root, Path.Combine(_tempRoot, "out-sibling", "x.m4b")));
        Assert.False(LibraryScanner.IsInside(root, root));
    }

    [Fact]
    public void SelectZipFiles_ContainsOnlyTopLevelOpusAndCover()
    {
        var bookDir = Path.Combine(_tempRoot, "Libro");
        Directory.CreateDirectory(Path.Combine(bookDir, "work"));
        File.WriteAllBytes(Path.Combine(bookDir, "001 - Inicio.opus"), [0x00]);
        File.WriteAllBytes(Path.Combine(bookDir, "cover.jpg"), [0x00]);
        File.WriteAllText(Path.Combine(bookDir, "notes.txt"), "x");
        File.WriteAllBytes(Path.Combine(bookDir, "work", "chapter_001.wav"), [0x00]);

        var names = LibraryScanner.SelectZipFiles(bookDir).Select(Path.GetFileName).ToList();

        Assert.Equal(["001 - Inicio.opus", "cover.jpg"], names);
    }
}
