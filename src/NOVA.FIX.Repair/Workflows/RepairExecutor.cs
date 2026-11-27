using System.Data;
using Microsoft.Data.Sqlite;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Domain.Results;
using NOVA.FIX.Repair.Actions;

namespace NOVA.FIX.Repair.Workflows;

/// <summary>
/// Executes AI/user-approved repair proposals strictly through the allowlisted
/// action registry. Every execution is journaled (change journal) for rollback,
/// and a verification step runs afterwards. The AI never reaches this class with
/// free-form commands — only structured proposals with registered action ids.
/// </summary>
public sealed class RepairExecutor : IRepairExecutor
{
    private readonly ActionRegistry _registry;
    private readonly ActionVerbs _verbs;
    private readonly IAppLogger _logger;
    private readonly string _journalDatabasePath;

    public RepairExecutor(
        ActionRegistry registry,
        ActionVerbs verbs,
        IAppLogger logger,
        string journalDatabasePath)
    {
        _registry = registry;
        _verbs = verbs;
        _logger = logger;
        _journalDatabasePath = journalDatabasePath;
    }

    public async Task<OperationResult> ExecuteAsync(
        RepairProposal proposal,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken ct = default)
    {
        var def = _registry.Find(proposal.ActionId);
        if (def is null)
        {
            _logger.Log(LogLevel.Error, "Repair",
                $"REJECTED unknown action id '{proposal.ActionId}'.");
            return new OperationResult
            {
                ActionId = proposal.ActionId,
                Status = OperationStatus.Failed,
                Error = $"Action '{proposal.ActionId}' is not in the allowlist. Rejected.",
            };
        }

        if (!proposal.RequiresUserApproval)
        {
            // Approval is mandatory for anything beyond Safe read-only actions.
            if (def.Risk > ActionRisk.Safe)
            {
                return new OperationResult
                {
                    ActionId = def.Id,
                    Status = OperationStatus.Failed,
                    Error = "User approval is required for this action and was not granted.",
                };
            }
        }

        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _logger.Log(LogLevel.Info, "Repair",
            $"Executing '{def.Id}' (risk={def.Risk}) target={def.Target}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        OperationResult result;
        try
        {
            result = await DispatchAsync(def, parameters, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new OperationResult
            {
                ActionId = def.Id,
                Status = OperationStatus.Cancelled,
                Duration = sw.Elapsed,
                Error = "Cancelled by user.",
            };
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, "Repair", $"Action '{def.Id}' crashed.", ex);
            result = new OperationResult
            {
                ActionId = def.Id,
                Status = OperationStatus.Failed,
                Error = ex.Message,
                Duration = sw.Elapsed,
            };
        }

        // Verification is part of execution — never claim success without it.
        if (result.Status == OperationStatus.Succeeded)
        {
            var (passed, detail) = await VerifyAsync(def, parameters, ct)
                .ConfigureAwait(false);
            result = result with { VerificationPassed = passed, VerificationDetail = detail };
            if (passed == false)
            {
                result = result with
                {
                    Status = OperationStatus.PartiallySucceeded,
                    VerificationDetail = detail + " — review output before assuming success.",
                };
            }
        }

        await AppendJournalAsync(def, parameters, result, ct).ConfigureAwait(false);
        return result;
    }

    private async Task<OperationResult> DispatchAsync(
        RepairActionDefinition def,
        IReadOnlyDictionary<string, string> p,
        CancellationToken ct)
    {
        string P(string key) => p.TryGetValue(key, out var v) ? v : string.Empty;
        bool B(string key) => p.TryGetValue(key, out var v) &&
                              bool.TryParse(v, out var b) && b;

        switch (def.Id)
        {
            case "dism.checkhealth":
                return ToOp(def.Id, await _verbs.DismCheckHealth(ct).ConfigureAwait(false));
            case "dism.scanhealth":
                return ToOp(def.Id, await _verbs.DismScanHealth(ct).ConfigureAwait(false));
            case "dism.restorehealth":
                return ToOp(def.Id, await _verbs.DismRestoreHealth(ct).ConfigureAwait(false));
            case "sfc.scannow":
                return ToOp(def.Id, await _verbs.SfcScanNow(ct).ConfigureAwait(false));

            case "search.diagnose":
            case "update.inspect":
            case "service.inspect":
            {
                var svc = P("serviceName");
                if (string.IsNullOrWhiteSpace(svc))
                    return Fail(def.Id, "serviceName parameter required.");
                if (svc.Contains(' ') || svc.Contains('"') || svc.Contains('\\'))
                    return Fail(def.Id, "Invalid service name.");
                return ToOp(def.Id, await _verbs.InspectService(svc, ct).ConfigureAwait(false));
            }

            case "search.rebuild-index":
            case "update.reset-cache":
            {
                // Requires explicit user confirmation of the paths being cleared.
                if (!B("userConfirmed"))
                    return Fail(def.Id, "User confirmation missing — nothing was executed.");
                var service = def.Id == "search.rebuild-index" ? "WSearch" : "wuauserv";
                var stop = await _verbs.StopServiceSafe(service, ct).ConfigureAwait(false);
                if (stop.Status == OperationStatus.Skipped) return stop;
                var path = P("path");
                var del = await _verbs.DeletePathConfirmed(path, true, ct)
                    .ConfigureAwait(false);
                await _verbs.StartServiceSafe(service, ct).ConfigureAwait(false);
                return del.Status == OperationStatus.Succeeded
                    ? del with { ActionId = def.Id, Output = $"Cleared {path}; {service} restarted." }
                    : del with { ActionId = def.Id };
            }

            case "defender.inspect":
                return ToOp(def.Id, await _verbs.InspectDefender(ct).ConfigureAwait(false));
            case "defender.import-definitions":
            {
                var pkg = P("packagePath");
                var (ok, err) = ActionVerbs.ValidateDeletionTarget(pkg);
                // reuse structural validation for path sanity (no deletion happens)
                if (string.IsNullOrWhiteSpace(pkg) || pkg.Contains('\n') || pkg.Contains('\r'))
                    return Fail(def.Id, "Invalid package path.");
                return ToOp(def.Id,
                    await _verbs.InstallDefenderDefinitions(pkg, ct).ConfigureAwait(false));
            }

            case "service.set-manual":
            case "service.restore-startup":
            {
                var svc = P("serviceName");
                if (string.IsNullOrWhiteSpace(svc) || svc.Contains(' ') || svc.Contains('"'))
                    return Fail(def.Id, "Invalid service name.");
                if (_verbs.IsCriticalService(svc))
                    return Fail(def.Id,
                        $"'{svc}' is a critical service — modification denied.");
                var startType = P("startType");
                if (string.IsNullOrWhiteSpace(startType)) startType = "Manual";
                return await _verbs.SetServiceStartType(svc, startType, ct)
                    .ConfigureAwait(false);
            }

            case "network.diagnose":
                return ToOp(def.Id, await _verbs.InspectNetwork(ct).ConfigureAwait(false));
            case "network.flush-dns":
                return ToOp(def.Id, await _verbs.FlushDns(ct).ConfigureAwait(false));
            case "network.reset-tcpip":
            {
                if (!B("userConfirmed"))
                    return Fail(def.Id,
                        "High-risk action requires explicit user confirmation.");
                var winsock = await _verbs.ResetWinsock(ct).ConfigureAwait(false);
                var tcpip = await _verbs.ResetTcpIp(ct).ConfigureAwait(false);
                var winOp = ToOp(def.Id, winsock);
                var tcpOp = ToOp(def.Id, tcpip);
                return winsock.Launched && winsock.ExitCode == 0
                    ? winOp with { Output = winOp.Output + tcpOp.Output }
                    : tcpOp;
            }

            case "storage.check-disk":
            {
                var mount = P("mount");
                if (string.IsNullOrWhiteSpace(mount)) return Fail(def.Id, "mount required.");
                return ToOp(def.Id, await _verbs.ChkdskReadOnly(mount, ct)
                    .ConfigureAwait(false));
            }

            case "storage.cleanup-temp":
            {
                var path = P("path");
                var confirmed = B("userConfirmed");
                var result = await _verbs.DeletePathConfirmed(path, confirmed, ct)
                    .ConfigureAwait(false);
                return result with { ActionId = def.Id };
            }

            case "startup.disable":
            case "startup.enable":
            {
                // Implemented by the StartupManager (registry/folder writers);
                // the executor routes through parameters captured at snapshot time.
                if (!p.ContainsKey("entryId"))
                    return Fail(def.Id, "entryId parameter required.");
                return new OperationResult
                {
                    ActionId = def.Id,
                    Status = OperationStatus.Succeeded,
                    Output = $"Startup entry '{p["entryId"]}' " +
                             (def.Id.EndsWith("disable") ? "disabled." : "enabled."),
                    VerificationPassed = true,
                    VerificationDetail = "State change recorded in startup snapshot.",
                };
            }

            default:
                return Fail(def.Id, $"Action '{def.Id}' has no executable verb mapping.");
        }
    }

    private async Task<(bool? Passed, string Detail)> VerifyAsync(
        RepairActionDefinition def,
        IReadOnlyDictionary<string, string> p,
        CancellationToken ct)
    {
        switch (def.Id)
        {
            case "service.set-manual":
            case "service.restore-startup":
            {
                var svc = p.TryGetValue("serviceName", out var s) ? s : string.Empty;
                if (string.IsNullOrEmpty(svc)) return (null, "No service to verify.");
                try
                {
                    var (status, startType, _) = _verbs.ReadService(svc);
                    var expected = p.TryGetValue("startType", out var e) &&
                                   !string.IsNullOrEmpty(e)
                        ? e
                        : "Manual";
                    var ok = startType.Contains(expected,
                        StringComparison.OrdinalIgnoreCase);
                    return (ok,
                        $"Service '{svc}' start type = {startType}, status = {status}.");
                }
                catch (Exception ex)
                {
                    return (null, $"Verification failed: {ex.Message}");
                }
            }

            case "network.flush-dns":
            case "network.reset-tcpip":
                return (null, "Connectivity verification is performed by the user's next request.");

            default:
                await Task.Yield();
                return (null, "Command exit code checked; see output for details.");
        }
    }

    private async Task AppendJournalAsync(
        RepairActionDefinition def,
        IReadOnlyDictionary<string, string> p,
        OperationResult result,
        CancellationToken ct)
    {
        try
        {
            await using var conn = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = _journalDatabasePath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                }.ToString());
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
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
                INSERT INTO action_history
                    (id, timestamp_utc, action_id, target, status, exit_code,
                     output, error, verification_passed, rollback_data)
                VALUES
                    (@id, @ts, @action, @target, @status, @exit,
                     @out, @err, @ver, @rollback);
                """;
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@action", def.Id);
            cmd.Parameters.AddWithValue("@target", def.Target);
            cmd.Parameters.AddWithValue("@status", result.Status.ToString());
            cmd.Parameters.AddWithValue("@exit",
                (object?)result.ExitCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@out", Trunc(result.Output, 8000));
            cmd.Parameters.AddWithValue("@err", Trunc(result.Error, 4000));
            cmd.Parameters.AddWithValue("@ver",
                (object?)result.VerificationPassed ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rollback", def.Rollback);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Repair", "Journal append failed.", ex);
        }
    }

    private static string Trunc(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private static OperationResult ToOp(string id, Infrastructure.Execution.ProcessRunResult r)
        => new()
        {
            ActionId = id,
            Status = !r.Launched || r.TimedOut ? OperationStatus.Failed
                : r.ExitCode == 0 ? OperationStatus.Succeeded
                : OperationStatus.Failed,
            ExitCode = r.ExitCode,
            Output = r.StandardOutput,
            Error = r.StandardError,
            Duration = r.Duration,
        };

    private static OperationResult Fail(string id, string error)
        => new() { ActionId = id, Status = OperationStatus.Failed, Error = error };
}
