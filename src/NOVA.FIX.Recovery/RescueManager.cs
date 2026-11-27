using System.Management;
using System.Text.Json;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.Recovery;

/// <summary>
/// Rescue &amp; Restore: restore-point inspection/creation, configuration
/// snapshots, change history and rollback. Every major modification is logged
/// with enough data to reverse it when technically possible. A restore point is
/// NEVER described as a complete personal-data backup.
/// </summary>
public sealed class RescueManager
{
    private readonly IAppLogger _logger;
    private readonly string _snapshotDirectory;
    private readonly string _journalDatabasePath;

    public RescueManager(
        IAppLogger logger, string snapshotDirectory, string journalDatabasePath)
    {
        _logger = logger;
        _snapshotDirectory = snapshotDirectory;
        _journalDatabasePath = journalDatabasePath;
        Directory.CreateDirectory(snapshotDirectory);
    }

    /// <summary>Human-readable disclaimer the UI must show alongside restore points.</summary>
    public const string RestorePointDisclaimer =
        "A restore point captures system files and settings only. It is NOT a " +
        "backup of your personal documents, photos, or projects.";

    public IReadOnlyList<RestorePointEntry> ListRestorePoints()
    {
        var list = new List<RestorePointEntry>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\default",
                "SELECT SequenceNumber, Description, CreationTime, RestorePointType " +
                "FROM SystemRestore");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                var created = o["CreationTime"]?.ToString();
                list.Add(new RestorePointEntry(
                    Id: o["SequenceNumber"]?.ToString() ?? "?",
                    Name: o["Description"]?.ToString() ?? "Restore point",
                    Created: DateTime.TryParse(created, out var d) ? d : DateTime.MinValue,
                    Description: $"Type {o["RestorePointType"]}",
                    Type: "SystemRestore"));
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Rescue",
                "SystemRestore WMI unavailable (may need elevation).", ex);
        }
        return list.OrderByDescending(r => r.Created).ToList();
    }

    public (bool Ok, string Message) CreateRestorePoint(string description)
    {
        try
        {
            // Requires administrator; the UI elevates explicitly when needed.
            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        "-NoProfile -NonInteractive -Command " +
                        $"\"Checkpoint-Computer -Description '" +
                        $"{description.Replace("'", "''")}' " +
                        "-RestorePointType MODIFY_SETTINGS\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            proc.Start();
            if (!proc.WaitForExit(120_000))
            {
                proc.Kill();
                return (false, "Restore point creation timed out.");
            }
            return proc.ExitCode == 0
                ? (true, "Restore point created.")
                : (false, $"Creation failed (exit {proc.ExitCode}). " +
                          "Administrator rights may be required.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Captures a JSON snapshot of arbitrary state for later rollback.</summary>
    public ConfigurationSnapshot CaptureSnapshot(
        string kind, object state, string description)
    {
        var id = Guid.NewGuid().ToString("N");
        var file = Path.Combine(_snapshotDirectory,
            $"{kind}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{id[..8]}.json");
        var json = JsonSerializer.Serialize(state,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(file, json);
        _logger.Log(LogLevel.Info, "Rescue",
            $"Snapshot {kind} captured: {Path.GetFileName(file)}");
        return new ConfigurationSnapshot(id, kind, DateTime.UtcNow, file,
            description, new FileInfo(file).Length);
    }

    public IReadOnlyList<ConfigurationSnapshot> ListSnapshots()
    {
        if (!Directory.Exists(_snapshotDirectory))
            return Array.Empty<ConfigurationSnapshot>();

        return Directory.EnumerateFiles(_snapshotDirectory, "*.json")
            .Select(f =>
            {
                var fi = new FileInfo(f);
                var kind = Path.GetFileName(f).Split('-')[0];
                return new ConfigurationSnapshot(
                    Id: fi.Name,
                    Kind: kind,
                    Created: fi.CreationTimeUtc,
                    FilePath: f,
                    Description: $"Snapshot file {fi.Name}",
                    SizeBytes: fi.Length);
            })
            .OrderByDescending(s => s.Created)
            .ToList();
    }

    /// <summary>Reads a snapshot's deserialized state for rollback processing.</summary>
    public T? ReadSnapshot<T>(string filePath)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(filePath));
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Rescue",
                $"Snapshot {filePath} unreadable.", ex);
            return default;
        }
    }

    /// <summary>Records a change in the change journal (called by engines).</summary>
    public async Task RecordChangeAsync(
        string module, string target, string description,
        string beforeState, string afterState, string? rollbackData,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = _journalDatabasePath,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
                }.ToString());
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
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
                INSERT INTO change_journal
                    (id, timestamp_utc, module, target, description,
                     before_state, after_state, rollback_data, rolled_back)
                VALUES
                    (@id, @ts, @module, @target, @desc, @before, @after, @rb, 0);
                """;
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@module", module);
            cmd.Parameters.AddWithValue("@target", target);
            cmd.Parameters.AddWithValue("@desc", description);
            cmd.Parameters.AddWithValue("@before", beforeState);
            cmd.Parameters.AddWithValue("@after", afterState);
            cmd.Parameters.AddWithValue("@rb",
                (object?)rollbackData ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Rescue", "Change journal write failed.", ex);
        }
    }

    /// <summary>Full change history for the Report Center and rollback UI.</summary>
    public async Task<IReadOnlyList<ChangeRecord>> HistoryAsync(
        int limit = 200, CancellationToken ct = default)
    {
        var list = new List<ChangeRecord>();
        if (!File.Exists(_journalDatabasePath)) return list;

        try
        {
            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = _journalDatabasePath,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                }.ToString());
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, timestamp_utc, module, target, description,
                       before_state, after_state, rollback_data, rolled_back
                FROM change_journal
                ORDER BY timestamp_utc DESC
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@limit", limit);
            await using var reader = await cmd.ExecuteReaderAsync(ct)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(new ChangeRecord(
                    Id: reader.GetString(0),
                    Timestamp: DateTime.Parse(reader.GetString(1)),
                    Module: reader.GetString(2),
                    Target: reader.GetString(3),
                    Description: reader.GetString(4),
                    BeforeState: reader.GetString(5),
                    AfterState: reader.GetString(6),
                    RollbackData: reader.IsDBNull(7) ? null : reader.GetString(7),
                    RolledBack: reader.GetInt32(8) == 1));
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Rescue", "History read failed.", ex);
        }
        return list;
    }
}
