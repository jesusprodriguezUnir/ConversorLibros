using ConversorLibros.Core.Models;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class DomainModelTests
{
    [Fact]
    public void Book_CanBeConstructedWithChapters()
    {
        var chapter = new Chapter(1, "Capítulo 1", "Texto de prueba.");
        var book = new Book("Mi Libro", "Autor", "es-ES", [chapter]);

        Assert.Equal("Mi Libro", book.Title);
        Assert.Single(book.Chapters);
        Assert.Equal("Capítulo 1", book.Chapters[0].Title);
    }
}
