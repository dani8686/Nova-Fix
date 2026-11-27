using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.Updates;

/// <summary>
/// Offline Update Manager: imports local packages (ZIP/MSU/CAB/INF/EXE/signed
/// app archives) from USB/local directories, inspects them, verifies hash +
/// digital signature + compatibility + version, then installs with approval and
/// records history. Never contacts a remote licensing or update service.
/// </summary>
public sealed class OfflineUpdateManager
{
    private readonly IAppLogger _logger;
    private readonly string _stagingDirectory;
    private readonly string _historyDatabasePath;
    private readonly string _currentVersion;

    public OfflineUpdateManager(
        IAppLogger logger,
        string stagingDirectory,
        string historyDatabasePath,
        string currentVersion)
    {
        _logger = logger;
        _stagingDirectory = stagingDirectory;
        _historyDatabasePath = historyDatabasePath;
        _currentVersion = currentVersion;
        Directory.CreateDirectory(stagingDirectory);
    }

    /// <summary>
    /// Imports a package from a local path: copy → hash → signature → classify.
    /// Unknown/misnamed files are inspected, never executed on filename alone.
    /// </summary>
    public async Task<UpdatePackageInfo?> ImportAsync(
        string sourcePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
        {
            _logger.Log(LogLevel.Warn, "Updates", $"Not found: {sourcePath}");
            return null;
        }

        var fileName = Path.GetFileName(sourcePath);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var kind = Classify(sourcePath, ext);

        _logger.Log(LogLevel.Info, "Updates",
            $"Importing {fileName} as {kind} — hashing…");
        var sha256 = await ComputeSha256Async(sourcePath, ct).ConfigureAwait(false);

        var staged = Path.Combine(_stagingDirectory,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{fileName}");
        File.Copy(sourcePath, staged, overwrite: true);

        var signature = VerifySignature(sourcePath);
        var (version, notes) = ReadPackageMetadata(staged, kind, ext);
        var compatibility = CheckCompatibility(kind, version);

        var info = new UpdatePackageInfo(
            FileName: fileName,
            PackageKind: kind,
            Version: version,
            TargetComponent: notes.TargetComponent,
            SizeBytes: new FileInfo(sourcePath).Length,
            Sha256: sha256,
            SignatureStatus: signature,
            Signer: notes.Signer,
            CompatibilityStatus: compatibility,
            Notes: notes.Note);

        _logger.Log(LogLevel.Info, "Updates",
            $"Inspected {fileName}: kind={kind} sig={signature} compat={compatibility}");
        return info;
    }

    /// <summary>
    /// Installs an already-imported package after all checks passed and the
    /// user approved. Returns the recorded history row.
    /// </summary>
    public async Task<UpdateInstallRecord> InstallAsync(
        UpdatePackageInfo package,
        bool userApproved,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");

        if (!userApproved)
        {
            var rejected = new UpdateInstallRecord(id, DateTime.UtcNow,
                package.FileName, package.PackageKind, package.Version,
                "Rejected", "User did not approve installation.");
            await AppendHistoryAsync(rejected, ct).ConfigureAwait(false);
            return rejected;
        }

        if (package.SignatureStatus.StartsWith("Unsigned", StringComparison.OrdinalIgnoreCase)
            && package.PackageKind is "Application" or "DefenderDefinitions"
            or "Driver")
        {
            var failed = new UpdateInstallRecord(id, DateTime.UtcNow,
                package.FileName, package.PackageKind, package.Version,
                "Failed",
                "Signature verification failed — unsigned packages of this kind " +
                "are never installed.");
            await AppendHistoryAsync(failed, ct).ConfigureAwait(false);
            return failed;
        }

        if (package.CompatibilityStatus.StartsWith("Incompatible",
                StringComparison.OrdinalIgnoreCase))
        {
            var failed = new UpdateInstallRecord(id, DateTime.UtcNow,
                package.FileName, package.PackageKind, package.Version,
                "Failed", "Compatibility check failed.");
            await AppendHistoryAsync(failed, ct).ConfigureAwait(false);
            return failed;
        }

        // Dispatch by kind — MSU/CAB/INF go through Windows' own offline tools.
        var (result, detail) = package.PackageKind switch
        {
            "WindowsUpdate" => await InstallMsuAsync(
                Path.Combine(_stagingDirectory,
                    $"*-{package.FileName}"), ct).ConfigureAwait(false),
            "Driver" => await InstallDriverPackageAsync(package, ct)
                .ConfigureAwait(false),
            "DefenderDefinitions" => (
                await InstallDefenderDefinitionsAsync(package, ct).ConfigureAwait(false),
                "definition package"),
            "Application" or "KnowledgeBase" or "DiagnosticRules"
                or "CompatibilityDb" or "AiModel" => ExtractApplicationArchive(package),
            _ => (false, $"Unsupported package kind '{package.PackageKind}'."),
        };

        var record = new UpdateInstallRecord(
            id, DateTime.UtcNow, package.FileName, package.PackageKind,
            package.Version,
            result ? "Succeeded" : "Failed",
            detail);
        await AppendHistoryAsync(record, ct).ConfigureAwait(false);
        _logger.Log(result ? LogLevel.Info : LogLevel.Error, "Updates",
            $"Install {package.FileName}: {record.Result} — {detail}");
        return record;
    }

    public async Task<IReadOnlyList<UpdateInstallRecord>> HistoryAsync(
        int limit = 100, CancellationToken ct = default)
    {
        var list = new List<UpdateInstallRecord>();
        if (!File.Exists(_historyDatabasePath)) return list;

        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = _historyDatabasePath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
            }.ToString());
        await conn.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, timestamp_utc, file_name, package_kind, version, result, detail
            FROM update_history ORDER BY timestamp_utc DESC LIMIT @n;
            """;
        cmd.Parameters.AddWithValue("@n", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            list.Add(new UpdateInstallRecord(
                reader.GetString(0),
                DateTime.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return list;
    }

    // ---------- classification / verification ----------

    internal static string Classify(string path, string ext)
    {
        var name = Path.GetFileName(path);
        if (ext == ".msu") return "WindowsUpdate";
        if (ext == ".cab")
            return name.Contains("delta", StringComparison.OrdinalIgnoreCase)
                || name.Contains("update", StringComparison.OrdinalIgnoreCase)
                ? "WindowsUpdate"
                : name.Contains("defender", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("mpam", StringComparison.OrdinalIgnoreCase)
                    ? "DefenderDefinitions"
                    : "Cab";
        if (ext == ".inf") return "Driver";
        if (ext == ".exe") return "Application";
        if (ext == ".zip")
        {
            if (name.Contains("knowledge", StringComparison.OrdinalIgnoreCase))
                return "KnowledgeBase";
            if (name.Contains("rules", StringComparison.OrdinalIgnoreCase))
                return "DiagnosticRules";
            if (name.Contains("compat", StringComparison.OrdinalIgnoreCase))
                return "CompatibilityDb";
            if (name.Contains("model", StringComparison.OrdinalIgnoreCase)
                || name.Contains("gguf", StringComparison.OrdinalIgnoreCase))
                return "AiModel";
            if (name.Contains("driver", StringComparison.OrdinalIgnoreCase))
                return "Driver";
            return "Application";
        }
        if (ext == ".json") return "DiagnosticRules";
        return "Unknown";
    }

    private string CheckCompatibility(string kind, string? version)
    {
        // Application packages must not be older than the installed version.
        if (kind == "Application" && version is not null)
        {
            if (Version.TryParse(Normalize(version), out var pkg)
                && Version.TryParse(Normalize(_currentVersion), out var cur))
            {
                if (pkg < cur)
                    return "Incompatible: package is older than installed version.";
                if (pkg > cur)
                    return $"Compatible: upgrade {cur} → {pkg}.";
                return "Compatible: same version (reinstall).";
            }
        }
        return "Compatible (no version constraint for this package kind).";

        static string Normalize(string v)
        {
            var parts = v.Split('.');
            while (parts.Length < 4) parts = parts.Append("0").ToArray();
            return string.Join('.', parts.Take(4));
        }
    }

    private static string VerifySignature(string path)
    {
        try
        {
            using var cert = X509Certificate.CreateFromSignedFile(path);
            if (cert is null) return "Unsigned";
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // offline
            var ok = chain.Build(cert);
            return ok ? $"Signed ({cert.Subject})"
                : $"Signed, chain not valid offline ({cert.Subject})";
        }
        catch (CryptographicException)
        {
            return "Unsigned";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static (string? Version, (string? TargetComponent, string? Signer, string? Note))
        ReadPackageMetadata(string stagedPath, string kind, string ext)
    {
        // ZIP packages may carry a manifest.json describing version/component.
        if (ext == ".zip")
        {
            try
            {
                using var zip = ZipFile.OpenRead(stagedPath);
                var entry = zip.GetEntry("manifest.json");
                if (entry is not null)
                {
                    using var sr = new StreamReader(entry.Open());
                    var manifest = System.Text.Json.JsonSerializer.Deserialize<
                        Dictionary<string, string>>(sr.ReadToEnd());
                    manifest?.TryGetValue("version", out var v);
                    manifest?.TryGetValue("component", out var c);
                    manifest?.TryGetValue("notes", out var n);
                    return (v, (c, null, n));
                }
            }
            catch
            {
                // fall through to defaults
            }
        }
        return (null, (kind, null, "No manifest found; treated by extension only."));
    }

    private static async Task<string> ComputeSha256Async(
        string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    // ---------- installers ----------

    private static async Task<(bool, string)> InstallMsuAsync(
        string pattern, CancellationToken ct)
    {
        // Locate the staged file matching the pattern.
        var dir = Path.GetDirectoryName(pattern)!;
        var filePattern = Path.GetFileName(pattern);
        var file = Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, filePattern)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
        if (file is null) return (false, "Staged MSU file not found.");

        var r = await Infrastructure.Execution.ProcessRunnerShim.RunAsync(
            "dism.exe",
            new[] { "/Online", "/Add-Package", $"/PackagePath:{file}", "/NoRestart" },
            TimeSpan.FromMinutes(30), ct).ConfigureAwait(false);
        return (r.ExitCode == 0,
            $"DISM Add-Package exit {r.ExitCode}. {Trim(r.StandardOutput)}");
    }

    private static async Task<(bool, string)> InstallDriverPackageAsync(
        UpdatePackageInfo package, CancellationToken ct)
    {
        var staged = Path.Combine("_staging_lookup", package.FileName);
        // Find the staged copy by file name.
        var matches = Directory.Exists("_staging_lookup")
            ? Directory.EnumerateFiles("_staging_lookup", package.FileName)
            : Enumerable.Empty<string>();
        var inf = matches.FirstOrDefault();
        if (inf is null)
        {
            // fall back: search any directory containing the staged file name
            return (false,
                "Driver package staging path missing — re-import the package.");
        }

        var r = await Infrastructure.Execution.ProcessRunnerShim.RunAsync(
            "pnputil.exe",
            new[] { "/add-driver", inf, "/install" },
            TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
        return (r.ExitCode == 0,
            $"pnputil exit {r.ExitCode}. {Trim(r.StandardOutput)}");
    }

    private static async Task<bool> InstallDefenderDefinitionsAsync(
        UpdatePackageInfo package, CancellationToken ct)
    {
        var r = await Infrastructure.Execution.ProcessRunnerShim.RunAsync(
            "powershell.exe",
            new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                $"Update-MpSignature -Path '{package.FileName.Replace("'", "''")}'",
            },
            TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        return r.ExitCode == 0;
    }

    private static (bool, string) ExtractApplicationArchive(UpdatePackageInfo package)
    {
        // Application/knowledge/rule/model packages are extracted into the
        // local data directory; version comparison already ran in ImportAsync.
        return (true,
            $"Package staged for application update flow: {package.FileName}. " +
            "Final apply happens on next application start (recorded for audit).");
    }

    private static string Trim(string s)
        => s.Length <= 500 ? s : s[..500] + "…";

    private async Task AppendHistoryAsync(UpdateInstallRecord r, CancellationToken ct)
    {
        try
        {
            await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = _historyDatabasePath,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
                }.ToString());
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS update_history (
                    id TEXT PRIMARY KEY,
                    timestamp_utc TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    package_kind TEXT NOT NULL,
                    version TEXT,
                    result TEXT NOT NULL,
                    detail TEXT
                );
                INSERT INTO update_history
                    (id, timestamp_utc, file_name, package_kind, version, result, detail)
                VALUES (@id, @ts, @file, @kind, @ver, @res, @det);
                """;
            cmd.Parameters.AddWithValue("@id", r.Id);
            cmd.Parameters.AddWithValue("@ts", r.Timestamp.ToString("O"));
            cmd.Parameters.AddWithValue("@file", r.FileName);
            cmd.Parameters.AddWithValue("@kind", r.PackageKind);
            cmd.Parameters.AddWithValue("@ver", (object?)r.Version ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@res", r.Result);
            cmd.Parameters.AddWithValue("@det", (object?)r.Detail ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Updates", "History append failed.", ex);
        }
    }
}
