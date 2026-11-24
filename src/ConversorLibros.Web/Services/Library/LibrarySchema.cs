using Microsoft.Data.Sqlite;

namespace ConversorLibros.Web.Services.Library;

public static class LibrarySchema
{
    public const int CurrentVersion = 1;

    public static void EnsureCreated(SqliteConnection connection)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
        pragma.ExecuteNonQuery();

        using var versionCmd = connection.CreateCommand();
        versionCmd.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(versionCmd.ExecuteScalar());

        if (version >= CurrentVersion)
        {
            return;
        }

        using var tx = connection.BeginTransaction();
        using var ddl = connection.CreateCommand();
        ddl.Transaction = tx;
        ddl.CommandText = """
            CREATE TABLE IF NOT EXISTS books (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                sort_title TEXT NOT NULL,
                author TEXT,
                series TEXT,
                series_index REAL,
                publisher TEXT,
                published_date TEXT,
                isbn TEXT,
                description TEXT,
                language TEXT,
                epub_path TEXT,
                epub_last_write_utc TEXT,
                source_sha256 TEXT,
                folder_path TEXT,
                is_legacy INTEGER NOT NULL DEFAULT 0,
                has_epub INTEGER NOT NULL DEFAULT 0,
                has_cover INTEGER NOT NULL DEFAULT 0,
                audio_status TEXT NOT NULL DEFAULT 'none',
                audio_format TEXT,
                audio_duration_ms INTEGER,
                chapter_count INTEGER NOT NULL DEFAULT 0,
                voice_used TEXT,
                reading_chapter INTEGER,
                reading_percent REAL NOT NULL DEFAULT 0,
                last_read_at TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS tags (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE
            );

            CREATE TABLE IF NOT EXISTS book_tags (
                book_id TEXT NOT NULL REFERENCES books(id) ON DELETE CASCADE,
                tag_id INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
                PRIMARY KEY (book_id, tag_id)
            );

            CREATE TABLE IF NOT EXISTS conversion_jobs (
                id TEXT PRIMARY KEY,
                book_id TEXT NOT NULL REFERENCES books(id) ON DELETE CASCADE,
                status TEXT NOT NULL,
                options_json TEXT NOT NULL,
                progress_percent REAL NOT NULL DEFAULT 0,
                current_stage TEXT,
                error_message TEXT,
                created_at TEXT NOT NULL,
                started_at TEXT,
                finished_at TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_books_sha256 ON books(source_sha256);
            CREATE INDEX IF NOT EXISTS idx_books_epub_path ON books(epub_path);
            CREATE INDEX IF NOT EXISTS idx_books_author ON books(author);
            CREATE INDEX IF NOT EXISTS idx_books_series ON books(series);
            CREATE INDEX IF NOT EXISTS idx_jobs_status ON conversion_jobs(status);
            CREATE INDEX IF NOT EXISTS idx_jobs_book_id ON conversion_jobs(book_id);

            PRAGMA user_version = 1;
            """;
        ddl.ExecuteNonQuery();
        tx.Commit();
    }
}
