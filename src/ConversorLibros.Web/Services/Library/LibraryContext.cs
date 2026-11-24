using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ConversorLibros.Web.Services.Library;

public sealed class LibraryContext(JsonSettingsStore settingsStore) : ILibraryContext
{
    public const string CacheFolderName = ".library-cache";
    private readonly object _initGate = new();
    private string? _initializedDbPath;

    public string OutputRoot => settingsStore.Get().OutputRoot;

    public string BooksRoot => settingsStore.Get().BooksRoot;

    public string DatabasePath => Path.Combine(OutputRoot, "library.db");

    public string GetBookCacheDir(string bookId) => Path.Combine(OutputRoot, CacheFolderName, bookId);

    public void EnsureInitialized()
    {
        var dbPath = DatabasePath;
        lock (_initGate)
        {
            if (string.Equals(_initializedDbPath, dbPath, StringComparison.OrdinalIgnoreCase) && File.Exists(dbPath))
            {
                return;
            }

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var conn = CreateRawConnection(dbPath);
            conn.Open();
            LibrarySchema.EnsureCreated(conn);
            _initializedDbPath = dbPath;
        }
    }

    public SqliteConnection OpenConnection()
    {
        EnsureInitialized();
        var conn = CreateRawConnection(DatabasePath);
        conn.Open();
        RegisterUnicodeHelpers(conn);
        using var fk = conn.CreateCommand();
        fk.CommandText = "PRAGMA foreign_keys = ON;";
        fk.ExecuteNonQuery();
        return conn;
    }

    private static void RegisterUnicodeHelpers(SqliteConnection conn)
    {
        var compareInfo = CultureInfo.InvariantCulture.CompareInfo;
        conn.CreateCollation("UNICODE_NOCASE", (a, b) =>
            compareInfo.Compare(a ?? string.Empty, b ?? string.Empty, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace));
        conn.CreateFunction<string?, string?, bool>("CONTAINS_TEXT", (haystack, needle) =>
        {
            if (string.IsNullOrEmpty(needle))
            {
                return true;
            }

            if (string.IsNullOrEmpty(haystack))
            {
                return false;
            }

            return compareInfo.IndexOf(haystack, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        });
    }

    private static SqliteConnection CreateRawConnection(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        return new SqliteConnection(builder.ToString());
    }
}
