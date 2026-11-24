using Microsoft.Data.Sqlite;

namespace ConversorLibros.Web.Services.Library;

public interface ILibraryContext
{
    string OutputRoot { get; }
    string BooksRoot { get; }
    string DatabasePath { get; }
    string GetBookCacheDir(string bookId);
    SqliteConnection OpenConnection();
    void EnsureInitialized();
}
