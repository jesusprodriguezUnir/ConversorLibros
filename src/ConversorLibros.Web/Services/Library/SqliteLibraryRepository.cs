using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ConversorLibros.Web.Services.Library;

public sealed class SqliteLibraryRepository(ILibraryContext context)
{
    public async Task<BookRecord> UpsertBookAsync(
        BookRecord book,
        IReadOnlyList<string> tags,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var createdAt = book.CreatedAt == default ? now : book.CreatedAt;
        var updatedAt = book.UpdatedAt == default ? now : book.UpdatedAt;
        var sortTitle = string.IsNullOrWhiteSpace(book.SortTitle) ? book.Title : book.SortTitle;

        await using var conn = context.OpenConnection();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO books (
                    id, title, sort_title, author, series, series_index, publisher, published_date,
                    isbn, description, language, epub_path, epub_last_write_utc, source_sha256,
                    folder_path, is_legacy, has_epub, has_cover, audio_status, audio_format,
                    audio_duration_ms, chapter_count, voice_used, reading_chapter, reading_percent,
                    last_read_at, created_at, updated_at
                ) VALUES (
                    @id, @title, @sort_title, @author, @series, @series_index, @publisher, @published_date,
                    @isbn, @description, @language, @epub_path, @epub_last_write_utc, @source_sha256,
                    @folder_path, @is_legacy, @has_epub, @has_cover, @audio_status, @audio_format,
                    @audio_duration_ms, @chapter_count, @voice_used, @reading_chapter, @reading_percent,
                    @last_read_at, @created_at, @updated_at
                )
                ON CONFLICT(id) DO UPDATE SET
                    title = excluded.title,
                    sort_title = excluded.sort_title,
                    author = excluded.author,
                    series = excluded.series,
                    series_index = excluded.series_index,
                    publisher = excluded.publisher,
                    published_date = excluded.published_date,
                    isbn = excluded.isbn,
                    description = excluded.description,
                    language = excluded.language,
                    epub_path = excluded.epub_path,
                    epub_last_write_utc = excluded.epub_last_write_utc,
                    source_sha256 = excluded.source_sha256,
                    folder_path = excluded.folder_path,
                    is_legacy = excluded.is_legacy,
                    has_epub = excluded.has_epub,
                    has_cover = excluded.has_cover,
                    audio_status = excluded.audio_status,
                    audio_format = excluded.audio_format,
                    audio_duration_ms = excluded.audio_duration_ms,
                    chapter_count = excluded.chapter_count,
                    voice_used = excluded.voice_used,
                    reading_chapter = excluded.reading_chapter,
                    reading_percent = excluded.reading_percent,
                    last_read_at = excluded.last_read_at,
                    updated_at = excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("@id", book.Id);
            cmd.Parameters.AddWithValue("@title", book.Title);
            cmd.Parameters.AddWithValue("@sort_title", sortTitle);
            cmd.Parameters.AddWithValue("@author", DbVal(book.Author));
            cmd.Parameters.AddWithValue("@series", DbVal(book.Series));
            cmd.Parameters.AddWithValue("@series_index", DbVal(book.SeriesIndex));
            cmd.Parameters.AddWithValue("@publisher", DbVal(book.Publisher));
            cmd.Parameters.AddWithValue("@published_date", DbVal(book.PublishedDate));
            cmd.Parameters.AddWithValue("@isbn", DbVal(book.Isbn));
            cmd.Parameters.AddWithValue("@description", DbVal(book.Description));
            cmd.Parameters.AddWithValue("@language", DbVal(book.Language));
            cmd.Parameters.AddWithValue("@epub_path", DbVal(book.EpubPath));
            cmd.Parameters.AddWithValue("@epub_last_write_utc", DbVal(FormatDate(book.EpubLastWriteUtc)));
            cmd.Parameters.AddWithValue("@source_sha256", DbVal(book.SourceSha256));
            cmd.Parameters.AddWithValue("@folder_path", DbVal(book.FolderPath));
            cmd.Parameters.AddWithValue("@is_legacy", book.IsLegacy ? 1 : 0);
            cmd.Parameters.AddWithValue("@has_epub", book.HasEpub ? 1 : 0);
            cmd.Parameters.AddWithValue("@has_cover", book.HasCover ? 1 : 0);
            cmd.Parameters.AddWithValue("@audio_status", string.IsNullOrWhiteSpace(book.AudioStatus) ? "none" : book.AudioStatus);
            cmd.Parameters.AddWithValue("@audio_format", DbVal(book.AudioFormat));
            cmd.Parameters.AddWithValue("@audio_duration_ms", DbVal(book.AudioDurationMs));
            cmd.Parameters.AddWithValue("@chapter_count", book.ChapterCount);
            cmd.Parameters.AddWithValue("@voice_used", DbVal(book.VoiceUsed));
            cmd.Parameters.AddWithValue("@reading_chapter", DbVal(book.ReadingChapter));
            cmd.Parameters.AddWithValue("@reading_percent", book.ReadingPercent);
            cmd.Parameters.AddWithValue("@last_read_at", DbVal(FormatDate(book.LastReadAt)));
            cmd.Parameters.AddWithValue("@created_at", FormatDate(createdAt)!);
            cmd.Parameters.AddWithValue("@updated_at", FormatDate(updatedAt)!);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        var normalizedTags = await ReplaceTagsAsync(conn, tx, book.Id, tags, ct);
        await tx.CommitAsync(ct);

        return book with
        {
            SortTitle = sortTitle,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            Tags = normalizedTags,
        };
    }

    public async Task<BookRecord?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var conn = context.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM books WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var record = ReadBook(reader);
        var tags = await LoadTagsForBooksAsync(conn, [record.Id], ct);
        return record with { Tags = tags.GetValueOrDefault(record.Id, []) };
    }

    public async Task<BookRecord?> FindBySha256Async(string sha256, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sha256))
        {
            return null;
        }

        await using var conn = context.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM books WHERE source_sha256 = @sha LIMIT 1;";
        cmd.Parameters.AddWithValue("@sha", sha256);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var record = ReadBook(reader);
        var tags = await LoadTagsForBooksAsync(conn, [record.Id], ct);
        return record with { Tags = tags.GetValueOrDefault(record.Id, []) };
    }

    public async Task<BookRecord?> FindByEpubPathAsync(string epubPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(epubPath))
        {
            return null;
        }

        await using var conn = context.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM books WHERE epub_path = @path COLLATE NOCASE LIMIT 1;";
        cmd.Parameters.AddWithValue("@path", epubPath);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var record = ReadBook(reader);
        var tags = await LoadTagsForBooksAsync(conn, [record.Id], ct);
        return record with { Tags = tags.GetValueOrDefault(record.Id, []) };
    }

    public async Task<IReadOnlyList<BookRecord>> ListAllAsync(CancellationToken ct = default)
    {
        await using var conn = context.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM books ORDER BY sort_title COLLATE NOCASE ASC;";

        var list = new List<BookRecord>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                list.Add(ReadBook(reader));
            }
        }

        var tagMap = await LoadTagsForBooksAsync(conn, list.Select(b => b.Id).ToList(), ct);
        return list.Select(b => b with { Tags = tagMap.GetValueOrDefault(b.Id, []) }).ToList();
    }

    public async Task<LibrarySearchResult> QueryAsync(LibraryQuery query, CancellationToken ct = default)
    {
        await using var conn = context.OpenConnection();

        var where = new List<string>();
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            var term = query.Query.Trim();
            where.Add("""
                (
                    CONTAINS_TEXT(b.title, @q)
                    OR CONTAINS_TEXT(b.author, @q)
                    OR CONTAINS_TEXT(b.series, @q)
                    OR CONTAINS_TEXT(b.description, @q)
                    OR EXISTS (
                        SELECT 1 FROM book_tags bt
                        JOIN tags t ON t.id = bt.tag_id
                        WHERE bt.book_id = b.id AND CONTAINS_TEXT(t.name, @q)
                    )
                )
                """);
            parameters.Add(("@q", term));
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            where.Add("b.author = @author COLLATE UNICODE_NOCASE");
            parameters.Add(("@author", query.Author.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.Series))
        {
            where.Add("b.series = @series COLLATE UNICODE_NOCASE");
            parameters.Add(("@series", query.Series.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            where.Add("""
                EXISTS (
                    SELECT 1 FROM book_tags bt
                    JOIN tags t ON t.id = bt.tag_id
                    WHERE bt.book_id = b.id AND t.name = @tag COLLATE UNICODE_NOCASE
                )
                """);
            parameters.Add(("@tag", query.Tag.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.AudioStatus))
        {
            var status = query.AudioStatus.Trim().ToLowerInvariant();
            if (status == "converting")
            {
                where.Add("b.audio_status IN ('queued', 'converting')");
            }
            else
            {
                where.Add("b.audio_status = @audioStatus");
                parameters.Add(("@audioStatus", status));
            }
        }

        var direction = string.Equals(query.Dir, "desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
        var orderBy = (query.Sort ?? "title").Trim().ToLowerInvariant() switch
        {
            "author" => $"COALESCE(b.author, '') COLLATE NOCASE {direction}, b.sort_title COLLATE NOCASE ASC",
            "series" => $"COALESCE(b.series, '') COLLATE NOCASE {direction}, COALESCE(b.series_index, 0) {direction}, b.sort_title COLLATE NOCASE ASC",
            "created_at" => $"b.created_at {direction}",
            "last_read_at" => $"COALESCE(b.last_read_at, '') {direction}, b.sort_title COLLATE NOCASE ASC",
            _ => $"b.sort_title COLLATE NOCASE {direction}",
        };

        var sql = new StringBuilder("SELECT b.* FROM books b");
        if (where.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", where));
        }

        sql.Append(" ORDER BY ").Append(orderBy).Append(';');

        var items = new List<BookRecord>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql.ToString();
            foreach (var (name, value) in parameters)
            {
                cmd.Parameters.AddWithValue(name, value);
            }

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add(ReadBook(reader));
            }
        }

        var tagMap = await LoadTagsForBooksAsync(conn, items.Select(b => b.Id).ToList(), ct);
        var hydrated = items.Select(b => b with { Tags = tagMap.GetValueOrDefault(b.Id, []) }).ToList();
        var facets = await LoadFacetsAsync(conn, ct);

        return new LibrarySearchResult(hydrated, hydrated.Count, facets);
    }

    public async Task<BookRecord?> UpdateMetadataAsync(
        string id,
        UpdateBookMetadataRequest patch,
        CancellationToken ct = default)
    {
        var existing = await GetByIdAsync(id, ct);
        if (existing is null)
        {
            return null;
        }

        var newTitle = string.IsNullOrWhiteSpace(patch.Title) ? existing.Title : patch.Title.Trim();
        var updated = existing with
        {
            Title = newTitle,
            SortTitle = newTitle,
            Author = patch.Author is not null ? NormalizeOptional(patch.Author) : existing.Author,
            Series = patch.Series is not null ? NormalizeOptional(patch.Series) : existing.Series,
            SeriesIndex = patch.SeriesIndex ?? existing.SeriesIndex,
            Publisher = patch.Publisher is not null ? NormalizeOptional(patch.Publisher) : existing.Publisher,
            PublishedDate = patch.PublishedDate is not null ? NormalizeOptional(patch.PublishedDate) : existing.PublishedDate,
            Isbn = patch.Isbn is not null ? NormalizeOptional(patch.Isbn) : existing.Isbn,
            Description = patch.Description is not null ? NormalizeOptional(patch.Description) : existing.Description,
            Language = patch.Language is not null ? NormalizeOptional(patch.Language) : existing.Language,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var tags = patch.Tags ?? existing.Tags;
        return await UpsertBookAsync(updated, tags, ct);
    }

    public async Task UpdateCoverFlagAsync(string id, bool hasCover, CancellationToken ct = default)
    {
        await using var conn = context.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE books SET has_cover = @hasCover, updated_at = @now WHERE id = @id;";
        cmd.Parameters.AddWithValue("@hasCover", hasCover ? 1 : 0);
        cmd.Parameters.AddWithValue("@now", FormatDate(DateTimeOffset.UtcNow)!);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateAudioStateAsync(
        string id,
        string audioStatus,
        string? audioFormat,
        long? durationMs,
        string? voiceUsed,
        CancellationToken ct = default)
    {
        await using var conn = context.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE books
            SET audio_status = @status,
                audio_format = @format,
                audio_duration_ms = @durationMs,
                voice_used = @voice,
                updated_at = @now
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@status", audioStatus);
        cmd.Parameters.AddWithValue("@format", DbVal(audioFormat));
        cmd.Parameters.AddWithValue("@durationMs", DbVal(durationMs));
        cmd.Parameters.AddWithValue("@voice", DbVal(voiceUsed));
        cmd.Parameters.AddWithValue("@now", FormatDate(DateTimeOffset.UtcNow)!);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateReadingProgressAsync(
        string id,
        int chapterIndex,
        double percent,
        DateTimeOffset lastReadAt,
        CancellationToken ct = default)
    {
        await using var conn = context.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE books
            SET reading_chapter = @chapter,
                reading_percent = @percent,
                last_read_at = @lastReadAt,
                updated_at = @now
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@chapter", chapterIndex);
        cmd.Parameters.AddWithValue("@percent", Math.Clamp(percent, 0d, 100d));
        cmd.Parameters.AddWithValue("@lastReadAt", FormatDate(lastReadAt)!);
        cmd.Parameters.AddWithValue("@now", FormatDate(DateTimeOffset.UtcNow)!);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> DeleteBookAsync(string id, CancellationToken ct = default)
    {
        await using var conn = context.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM books WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<bool> HasActiveOrQueuedJobsAsync(CancellationToken ct = default)
    {
        await using var conn = context.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM conversion_jobs WHERE status IN ('queued', 'running'));";
        var result = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        return result == 1;
    }

    public async Task<bool> HasActiveOrQueuedJobForBookAsync(string bookId, CancellationToken ct = default)
    {
        await using var conn = context.OpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM conversion_jobs WHERE book_id = @bookId AND status IN ('queued', 'running'));";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        var result = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        return result == 1;
    }

    private static async Task<IReadOnlyList<string>> ReplaceTagsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        string bookId,
        IReadOnlyList<string> rawTags,
        CancellationToken ct)
    {
        await using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM book_tags WHERE book_id = @bookId;";
            del.Parameters.AddWithValue("@bookId", bookId);
            await del.ExecuteNonQueryAsync(ct);
        }

        var normalized = rawTags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var tag in normalized)
        {
            await using (var insTag = conn.CreateCommand())
            {
                insTag.Transaction = tx;
                insTag.CommandText = "INSERT OR IGNORE INTO tags(name) VALUES (@name);";
                insTag.Parameters.AddWithValue("@name", tag);
                await insTag.ExecuteNonQueryAsync(ct);
            }

            long tagId;
            await using (var selTag = conn.CreateCommand())
            {
                selTag.Transaction = tx;
                selTag.CommandText = "SELECT id FROM tags WHERE name = @name COLLATE NOCASE LIMIT 1;";
                selTag.Parameters.AddWithValue("@name", tag);
                tagId = Convert.ToInt64(await selTag.ExecuteScalarAsync(ct));
            }

            await using var link = conn.CreateCommand();
            link.Transaction = tx;
            link.CommandText = "INSERT OR IGNORE INTO book_tags(book_id, tag_id) VALUES (@bookId, @tagId);";
            link.Parameters.AddWithValue("@bookId", bookId);
            link.Parameters.AddWithValue("@tagId", tagId);
            await link.ExecuteNonQueryAsync(ct);
        }

        return normalized;
    }

    private static async Task<Dictionary<string, IReadOnlyList<string>>> LoadTagsForBooksAsync(
        SqliteConnection conn,
        IReadOnlyList<string> bookIds,
        CancellationToken ct)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (bookIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        }

        await using var cmd = conn.CreateCommand();
        var placeholders = new List<string>(bookIds.Count);
        for (var i = 0; i < bookIds.Count; i++)
        {
            var p = $"@b{i}";
            placeholders.Add(p);
            cmd.Parameters.AddWithValue(p, bookIds[i]);
        }

        cmd.CommandText = $"""
            SELECT bt.book_id, t.name
            FROM book_tags bt
            JOIN tags t ON t.id = bt.tag_id
            WHERE bt.book_id IN ({string.Join(", ", placeholders)})
            ORDER BY t.name COLLATE NOCASE ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var bookId = reader.GetString(0);
            var tagName = reader.GetString(1);
            if (!result.TryGetValue(bookId, out var list))
            {
                list = [];
                result[bookId] = list;
            }

            list.Add(tagName);
        }

        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal);
    }

    private static async Task<LibraryFacetsDto> LoadFacetsAsync(SqliteConnection conn, CancellationToken ct)
    {
        int all = 0, withAudio = 0, textOnly = 0, converting = 0;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT
                    COUNT(*),
                    SUM(CASE WHEN audio_status = 'ready' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN audio_status = 'none' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN audio_status IN ('queued', 'converting') THEN 1 ELSE 0 END)
                FROM books;
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                all = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                withAudio = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                textOnly = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                converting = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            }
        }

        var authors = await ReadFacetListAsync(
            conn,
            "SELECT author, COUNT(*) FROM books WHERE author IS NOT NULL AND TRIM(author) <> '' GROUP BY author COLLATE NOCASE ORDER BY author COLLATE NOCASE ASC;",
            ct);
        var series = await ReadFacetListAsync(
            conn,
            "SELECT series, COUNT(*) FROM books WHERE series IS NOT NULL AND TRIM(series) <> '' GROUP BY series COLLATE NOCASE ORDER BY series COLLATE NOCASE ASC;",
            ct);
        var tags = await ReadFacetListAsync(
            conn,
            "SELECT t.name, COUNT(bt.book_id) FROM tags t JOIN book_tags bt ON bt.tag_id = t.id GROUP BY t.id ORDER BY t.name COLLATE NOCASE ASC;",
            ct);

        return new LibraryFacetsDto(
            new LibraryCountsDto(all, withAudio, textOnly, converting),
            authors,
            series,
            tags);
    }

    private static async Task<IReadOnlyList<FacetItemDto>> ReadFacetListAsync(
        SqliteConnection conn,
        string sql,
        CancellationToken ct)
    {
        var items = new List<FacetItemDto>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new FacetItemDto(reader.GetString(0), reader.GetInt32(1)));
        }

        return items;
    }

    private static BookRecord ReadBook(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Title = r.GetString(r.GetOrdinal("title")),
        SortTitle = r.GetString(r.GetOrdinal("sort_title")),
        Author = GetNullableString(r, "author"),
        Series = GetNullableString(r, "series"),
        SeriesIndex = GetNullableDouble(r, "series_index"),
        Publisher = GetNullableString(r, "publisher"),
        PublishedDate = GetNullableString(r, "published_date"),
        Isbn = GetNullableString(r, "isbn"),
        Description = GetNullableString(r, "description"),
        Language = GetNullableString(r, "language"),
        EpubPath = GetNullableString(r, "epub_path"),
        EpubLastWriteUtc = ParseNullableDate(GetNullableString(r, "epub_last_write_utc")),
        SourceSha256 = GetNullableString(r, "source_sha256"),
        FolderPath = GetNullableString(r, "folder_path"),
        IsLegacy = r.GetInt32(r.GetOrdinal("is_legacy")) != 0,
        HasEpub = r.GetInt32(r.GetOrdinal("has_epub")) != 0,
        HasCover = r.GetInt32(r.GetOrdinal("has_cover")) != 0,
        AudioStatus = r.GetString(r.GetOrdinal("audio_status")),
        AudioFormat = GetNullableString(r, "audio_format"),
        AudioDurationMs = GetNullableLong(r, "audio_duration_ms"),
        ChapterCount = r.GetInt32(r.GetOrdinal("chapter_count")),
        VoiceUsed = GetNullableString(r, "voice_used"),
        ReadingChapter = GetNullableInt(r, "reading_chapter"),
        ReadingPercent = r.GetDouble(r.GetOrdinal("reading_percent")),
        LastReadAt = ParseNullableDate(GetNullableString(r, "last_read_at")),
        CreatedAt = ParseDateOrDefault(r.GetString(r.GetOrdinal("created_at"))),
        UpdatedAt = ParseDateOrDefault(r.GetString(r.GetOrdinal("updated_at"))),
    };

    private static object DbVal(object? value) => value ?? DBNull.Value;

    private static string? NormalizeOptional(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? FormatDate(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseNullableDate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTimeOffset ParseDateOrDefault(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    private static string? GetNullableString(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetString(ord);
    }

    private static double? GetNullableDouble(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetDouble(ord);
    }

    private static long? GetNullableLong(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetInt64(ord);
    }

    private static int? GetNullableInt(SqliteDataReader r, string col)
    {
        var ord = r.GetOrdinal(col);
        return r.IsDBNull(ord) ? null : r.GetInt32(ord);
    }
}
