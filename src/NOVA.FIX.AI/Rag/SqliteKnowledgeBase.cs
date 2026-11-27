using System.Data;
using Microsoft.Data.Sqlite;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.AI.Rag;

/// <summary>
/// Local retrieval-augmented generation store backed by SQLite FTS5.
/// Seeds itself from bundled JSON knowledge articles on first run.
/// Never touches the network.
/// </summary>
public sealed class SqliteKnowledgeBase : IKnowledgeBase
{
    private readonly string _connectionString;
    private readonly string _seedPath;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public SqliteKnowledgeBase(string databasePath, string seedArticlesPath, IAppLogger logger)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        _seedPath = seedArticlesPath;
        _logger = logger;
    }

    public string DatabaseVersion { get; private set; } = "uninitialized";

    public DateTime DatabaseBuildDate { get; private set; } = DateTime.MinValue;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await ExecuteAsync(conn,
                """
                CREATE TABLE IF NOT EXISTS articles (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    category TEXT NOT NULL,
                    content TEXT NOT NULL,
                    tags TEXT NOT NULL DEFAULT '',
                    source TEXT NOT NULL DEFAULT 'NOVA FIX bundled reference',
                    version TEXT NOT NULL DEFAULT '1',
                    published_date TEXT
                );
                """, ct).ConfigureAwait(false);

            await ExecuteAsync(conn,
                """
                CREATE VIRTUAL TABLE IF NOT EXISTS articles_fts USING fts5(
                    title, content, tags,
                    content='articles',
                    content_rowid='rowid'
                );
                """, ct).ConfigureAwait(false);

            await ExecuteAsync(conn,
                """
                CREATE TABLE IF NOT EXISTS meta (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """, ct).ConfigureAwait(false);

            var count = await ScalarAsync(conn, "SELECT COUNT(*) FROM articles", ct)
                .ConfigureAwait(false);
            if (count == 0 && File.Exists(_seedPath))
            {
                await SeedAsync(conn, ct).ConfigureAwait(false);
            }

            DatabaseVersion = (await ScalarTextAsync(conn,
                "SELECT value FROM meta WHERE key='version'", ct).ConfigureAwait(false))
                ?? "1";
            var buildText = await ScalarTextAsync(conn,
                "SELECT value FROM meta WHERE key='build_date'", ct).ConfigureAwait(false);
            DatabaseBuildDate = DateTime.TryParse(buildText, out var d)
                ? d
                : new DateTime(2026, 11, 26);

            _initialized = true;
            _logger.Log(LogLevel.Info, "KnowledgeBase",
                $"Initialized: {await ScalarAsync(conn, "SELECT COUNT(*) FROM articles", ct)
                    .ConfigureAwait(false)} articles, version {DatabaseVersion}.");
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(
        string query, int maxResults = 5, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<KnowledgeHit>();

        var hits = new List<KnowledgeHit>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        // FTS5 with a fallback LIKE query when the FTS match syntax rejects input.
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT a.id, a.title, a.category, a.content, a.tags, a.source, a.version,
                       a.published_date,
                       bm25(articles_fts) AS score,
                       snippet(articles_fts, 1, '[', ']', '…', 12) AS snippet
                FROM articles_fts
                JOIN articles a ON a.rowid = articles_fts.rowid
                WHERE articles_fts MATCH @q
                ORDER BY score
                LIMIT @n;
                """;
            cmd.Parameters.AddWithValue("@q", BuildMatchQuery(query));
            cmd.Parameters.AddWithValue("@n", maxResults);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                hits.Add(ReadHit(reader, negateScore: true));
            }
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            // malformed MATCH expression — degrade to LIKE search, still fully offline
            _logger.Log(LogLevel.Warn, "KnowledgeBase",
                "FTS match rejected; falling back to LIKE.", ex);
            hits.Clear();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT a.id, a.title, a.category, a.content, a.tags, a.source, a.version,
                       a.published_date, 0.0 AS score,
                       substr(a.content, 1, 160) AS snippet
                FROM articles a
                WHERE a.title LIKE @q OR a.content LIKE @q OR a.tags LIKE @q
                LIMIT @n;
                """;
            cmd.Parameters.AddWithValue("@q", $"%{query}%");
            cmd.Parameters.AddWithValue("@n", maxResults);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                hits.Add(ReadHit(reader, negateScore: false));
            }
        }

        return hits;
    }

    public async Task<KnowledgeArticle?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, title, category, content, tags, source, version, published_date " +
            "FROM articles WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return ReadArticle(reader);
    }

    private static KnowledgeHit ReadHit(SqliteDataReader reader, bool negateScore)
    {
        var article = ReadArticle(reader);
        var scoreOrdinal = reader.GetOrdinal("score");
        var rawScore = reader.IsDBNull(scoreOrdinal) ? 0d : reader.GetDouble(scoreOrdinal);
        var score = negateScore ? -rawScore : rawScore;
        var snippetOrdinal = reader.GetOrdinal("snippet");
        var snippet = reader.IsDBNull(snippetOrdinal) ? string.Empty
            : reader.GetString(snippetOrdinal);
        return new KnowledgeHit(article, score, snippet, 0, snippet.Length);
    }

    private static KnowledgeArticle ReadArticle(SqliteDataReader reader)
    {
        string Str(string col)
        {
            var i = reader.GetOrdinal(col);
            return reader.IsDBNull(i) ? string.Empty : reader.GetString(i);
        }

        string? StrOrNull(string col)
        {
            var i = reader.GetOrdinal(col);
            return reader.IsDBNull(i) ? null : reader.GetString(i);
        }

        return new KnowledgeArticle(
            Id: Str("id"),
            Title: Str("title"),
            Category: Str("category"),
            Content: Str("content"),
            Tags: Str("tags").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Source: Str("source"),
            Version: Str("version"),
            PublishedDate: DateTime.TryParse(StrOrNull("published_date"), out var d) ? d : null);
    }

    /// <summary>
    /// Converts free text into a safe FTS5 match query: quoted terms ANDed.
    /// </summary>
    internal static string BuildMatchQuery(string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('"', '\''))
            .Where(t => t.Length > 0)
            .Select(t => $"\"{t.Replace("\"", "\"\"")}\"")
            .Take(12)
            .ToArray();
        return terms.Length == 0 ? "\"\"" : string.Join(" OR ", terms);
    }

    private async Task SeedAsync(SqliteConnection conn, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(_seedPath, ct).ConfigureAwait(false);
        var articles = System.Text.Json.JsonSerializer.Deserialize<List<SeedArticle>>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new List<SeedArticle>();

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct)
            .ConfigureAwait(false);
        foreach (var a in articles)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO articles
                    (id, title, category, content, tags, source, version, published_date)
                VALUES
                    (@id, @title, @category, @content, @tags, @source, @version, @date);
                """;
            cmd.Parameters.AddWithValue("@id", a.Id);
            cmd.Parameters.AddWithValue("@title", a.Title);
            cmd.Parameters.AddWithValue("@category", a.Category ?? "General");
            cmd.Parameters.AddWithValue("@content", a.Content);
            cmd.Parameters.AddWithValue("@tags", a.Tags ?? string.Empty);
            cmd.Parameters.AddWithValue("@source", a.Source ?? "NOVA FIX bundled reference");
            cmd.Parameters.AddWithValue("@version", a.Version ?? "1");
            cmd.Parameters.AddWithValue("@date", a.PublishedDate ?? (object)DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var meta = conn.CreateCommand())
        {
            meta.Transaction = tx;
            meta.CommandText = """
                INSERT OR REPLACE INTO meta(key, value) VALUES ('version', @v);
                INSERT OR REPLACE INTO meta(key, value) VALUES ('build_date', @d);
                """;
            meta.Parameters.AddWithValue("@v", articles.FirstOrDefault()?.Version ?? "1");
            meta.Parameters.AddWithValue("@d",
                articles.FirstOrDefault()?.PublishedDate ?? "2026-11-26");
            await meta.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<long> ScalarAsync(
        SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt64(result);
    }

    private static async Task<string?> ScalarTextAsync(
        SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is DBNull or null ? null : result.ToString();
    }

    private sealed class SeedArticle
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string Content { get; set; } = string.Empty;
        public string? Tags { get; set; }
        public string? Source { get; set; }
        public string? Version { get; set; }
        public string? PublishedDate { get; set; }
    }
}
