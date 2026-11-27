using Microsoft.Data.Sqlite;

namespace NOVA.FIX.Infrastructure.Data;

/// <summary>
/// Creates and migrates the local SQLite databases (diagnostic rules, knowledge
/// base with FTS5, event catalog, history). Fully offline; parameterized queries only.
/// </summary>
public sealed class DatabaseInitializer
{
    public const string SchemaVersion = "1";

    private readonly string _databaseDirectory;

    public DatabaseInitializer(string databaseDirectory)
    {
        _databaseDirectory = databaseDirectory;
        Directory.CreateDirectory(databaseDirectory);
    }

    public string MainDatabasePath => Path.Combine(_databaseDirectory, "novafix.db");
    public string KnowledgeDatabasePath => Path.Combine(_databaseDirectory, "knowledge.db");

    public void EnsureMainDatabase()
    {
        using var conn = Open(MainDatabasePath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = MainSchema;
        cmd.ExecuteNonQuery();
    }

    public void EnsureKnowledgeDatabase()
    {
        using var conn = Open(KnowledgeDatabasePath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = KnowledgeSchema;
        cmd.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        conn.Open();
        return conn;
    }

    private const string MainSchema = @"
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

CREATE TABLE IF NOT EXISTS schema_info (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT OR IGNORE INTO schema_info(key, value) VALUES ('version', '1');
INSERT OR IGNORE INTO schema_info(key, value) VALUES ('build_date', '2026-11-26');

CREATE TABLE IF NOT EXISTS diagnostic_rules (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    category TEXT NOT NULL,
    severity TEXT NOT NULL,
    condition_json TEXT NOT NULL,
    evidence_template TEXT NOT NULL,
    recommendation TEXT NOT NULL,
    risks TEXT NOT NULL DEFAULT '',
    verification TEXT NOT NULL DEFAULT '',
    enabled INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS event_catalog (
    id INTEGER NOT NULL,
    log_name TEXT NOT NULL,
    provider TEXT NOT NULL,
    title TEXT NOT NULL,
    description TEXT NOT NULL,
    severity TEXT NOT NULL,
    guidance TEXT NOT NULL,
    PRIMARY KEY (log_name, provider, id)
);

CREATE TABLE IF NOT EXISTS repair_definitions (
    id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    category TEXT NOT NULL,
    target TEXT NOT NULL,
    purpose TEXT NOT NULL,
    expected_result TEXT NOT NULL,
    risk TEXT NOT NULL,
    preconditions_json TEXT NOT NULL,
    required_permissions TEXT NOT NULL,
    verification TEXT NOT NULL,
    rollback TEXT NOT NULL,
    allowed_verbs_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS findings_history (
    id TEXT PRIMARY KEY,
    timestamp_utc TEXT NOT NULL,
    title TEXT NOT NULL,
    severity TEXT NOT NULL,
    confidence TEXT NOT NULL,
    component TEXT NOT NULL,
    detail_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS action_history (
    id TEXT PRIMARY KEY,
    timestamp_utc TEXT NOT NULL,
    action_id TEXT NOT NULL,
    target TEXT NOT NULL,
    status TEXT NOT NULL,
    exit_code INTEGER,
    output TEXT NOT NULL DEFAULT '',
    error TEXT NOT NULL DEFAULT '',
    verification_passed INTEGER,
    rollback_data TEXT
);

CREATE TABLE IF NOT EXISTS change_journal (
    id TEXT PRIMARY KEY,
    timestamp_utc TEXT NOT NULL,
    module TEXT NOT NULL,
    target TEXT NOT NULL,
    description TEXT NOT NULL,
    before_state TEXT NOT NULL,
    after_state TEXT NOT NULL,
    rollback_data TEXT,
    rolled_back INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS update_history (
    id TEXT PRIMARY KEY,
    timestamp_utc TEXT NOT NULL,
    file_name TEXT NOT NULL,
    package_kind TEXT NOT NULL,
    version TEXT,
    result TEXT NOT NULL,
    detail TEXT
);

CREATE INDEX IF NOT EXISTS idx_findings_time ON findings_history(timestamp_utc);
CREATE INDEX IF NOT EXISTS idx_actions_time ON action_history(timestamp_utc);
CREATE INDEX IF NOT EXISTS idx_journal_time ON change_journal(timestamp_utc);
";

    private const string KnowledgeSchema = @"
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

CREATE TABLE IF NOT EXISTS schema_info (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT OR IGNORE INTO schema_info(key, value) VALUES ('version', '1');
INSERT OR IGNORE INTO schema_info(key, value) VALUES ('build_date', '2026-11-26');
INSERT OR IGNORE INTO schema_info(key, value) VALUES ('source', 'NOVA FIX bundled offline knowledge base');

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

CREATE VIRTUAL TABLE IF NOT EXISTS articles_fts USING fts5(
    title, content, tags,
    content='articles',
    content_rowid='rowid'
);

CREATE TRIGGER IF NOT EXISTS articles_ai AFTER INSERT ON articles BEGIN
    INSERT INTO articles_fts(rowid, title, content, tags)
    VALUES (new.rowid, new.title, new.content, new.tags);
END;

CREATE TRIGGER IF NOT EXISTS articles_ad AFTER DELETE ON articles BEGIN
    INSERT INTO articles_fts(articles_fts, rowid, title, content, tags)
    VALUES ('delete', old.rowid, old.title, old.content, old.tags);
END;

CREATE TRIGGER IF NOT EXISTS articles_au AFTER UPDATE ON articles BEGIN
    INSERT INTO articles_fts(articles_fts, rowid, title, content, tags)
    VALUES ('delete', old.rowid, old.title, old.content, old.tags);
    INSERT INTO articles_fts(rowid, title, content, tags)
    VALUES (new.rowid, new.title, new.content, new.tags);
END;
";
}
