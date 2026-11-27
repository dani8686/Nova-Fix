using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;

namespace NOVA.FIX.Repair.Actions;

/// <summary>
/// The allowlisted action registry — the ONLY path to system modification.
/// AI-generated proposals must reference an action id here; anything else is
/// rejected. Each entry carries full safety metadata (target, purpose, risk,
/// preconditions, permissions, verification, rollback).
/// </summary>
public sealed class ActionRegistry : IActionRegistry
{
    private readonly Dictionary<string, RepairActionDefinition> _actions;

    public ActionRegistry()
    {
        _actions = Build().ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<RepairActionDefinition> All => _actions.Values.OrderBy(a => a.Category)
        .ThenBy(a => a.DisplayName).ToList();

    public RepairActionDefinition? Find(string actionId)
        => _actions.TryGetValue(actionId, out var def) ? def : null;

    /// <summary>True when an AI proposal's action id exists in the allowlist.</summary>
    public bool IsAllowed(string actionId) => _actions.ContainsKey(actionId);

    private static IEnumerable<RepairActionDefinition> Build()
    {
        // --- Windows component repair ---
        yield return Def(
            "dism.checkhealth",
            "DISM CheckHealth",
            "Windows Component",
            "Online component store",
            "Check the Windows component store for reported corruption.",
            "A corruption report without modifying anything.",
            ActionRisk.Safe,
            new[] { "Windows 10/11 online", "Read access to CBS log" },
            "Standard user",
            "DISM reports whether corruption markers were found.",
            "None — check only, no changes made.",
            new[] { "DismCheckHealth" });

        yield return Def(
            "dism.scanhealth",
            "DISM ScanHealth",
            "Windows Component",
            "Online component store",
            "Deep-scan the component store for corruption.",
            "A detailed scan result (takes 10–20 minutes).",
            ActionRisk.Low,
            new[] { "Sufficient free disk space (>5 GB)", "Power connected on laptops" },
            "Administrator",
            "DISM /ScanHealth reports no corruption or lists findings.",
            "None — scan only, no changes made.",
            new[] { "DismScanHealth" });

        yield return Def(
            "dism.restorehealth",
            "DISM RestoreHealth",
            "Windows Component",
            "Online component store",
            "Repair corrupted component store files using local sources.",
            "A healthy component store; fixes 0x800fxxxx update errors.",
            ActionRisk.Medium,
            new[] { "CheckHealth/ScanHealth confirmed corruption",
                    "Restore point created (setting enabled)",
                    "≥ 5 GB free disk space" },
            "Administrator",
            "Subsequent CheckHealth reports no corruption.",
            "Roll back via System Restore point created before the action.",
            new[] { "DismRestoreHealth" });

        yield return Def(
            "sfc.scannow",
            "SFC /scannow",
            "Windows Component",
            "Protected system files",
            "Verify and repair protected Windows system files.",
            "System files restored from the component store.",
            ActionRisk.Medium,
            new[] { "Component store healthy (run DISM first if not)" },
            "Administrator",
            "'Windows Resource Protection did not find any integrity violations.'",
            "Replaced files are backed up by SFC in %WinDir%\\WinSxS\\Backup.",
            new[] { "SfcScanNow" });

        // --- Windows Search ---
        yield return Def(
            "search.diagnose",
            "Windows Search Diagnosis",
            "Windows Search",
            "WSearch service & index",
            "Inspect WSearch service state, dependencies and index database.",
            "A read-only diagnosis report.",
            ActionRisk.Safe,
            new[] { "None" },
            "Standard user",
            "Report generated with service state and index size.",
            "None — read only.",
            new[] { "InspectService" });

        yield return Def(
            "search.rebuild-index",
            "Rebuild Windows Search Index",
            "Windows Search",
            "Search index database",
            "Delete and rebuild the local search index database.",
            "Working search results (index rebuilds in background).",
            ActionRisk.Low,
            new[] { "search.diagnose completed",
                    "User confirmed search history loss is acceptable" },
            "Administrator",
            "Indexer service restarts and index file regrows.",
            "Index rebuilds automatically; no user data affected.",
            new[] { "StopService", "DeletePathConfirmed", "StartService" });

        // --- Windows Update ---
        yield return Def(
            "update.inspect",
            "Windows Update Diagnosis",
            "Windows Update",
            "wuauserv & SoftwareDistribution",
            "Inspect update service state and cache size (read-only).",
            "A diagnosis report.",
            ActionRisk.Safe,
            new[] { "None" },
            "Standard user",
            "Report generated.",
            "None — read only.",
            new[] { "InspectService", "InspectPath" });

        yield return Def(
            "update.reset-cache",
            "Reset Windows Update Cache",
            "Windows Update",
            "SoftwareDistribution download cache",
            "Stop the update services, clear the download cache, restart services.",
            "Update checks proceed without cached-corruption errors.",
            ActionRisk.Medium,
            new[] { "update.inspect completed", "No update actively installing",
                    "User approval recorded" },
            "Administrator",
            "Next update check completes; cache folder regrows.",
            "Cache re-downloads automatically; installed updates unaffected.",
            new[] { "StopService", "DeletePathConfirmed", "StartService" });

        // --- Defender ---
        yield return Def(
            "defender.inspect",
            "Defender Status Inspection",
            "Defender",
            "Windows Defender",
            "Read protection status, signature age and real-time state.",
            "A status report with signature age.",
            ActionRisk.Safe,
            new[] { "None" },
            "Standard user",
            "Report includes signature version/date.",
            "None — read only.",
            new[] { "InspectDefender" });

        yield return Def(
            "defender.import-definitions",
            "Import Offline Defender Definitions",
            "Defender",
            "Security intelligence",
            "Install a locally imported, signed definition package.",
            "Updated security intelligence without internet.",
            ActionRisk.Low,
            new[] { "Definition package file selected by user",
                    "Package signature verified", "Defender operational" },
            "Administrator",
            "Signature version matches the imported package.",
            "Previous definitions remain until the next update cycle.",
            new[] { "InstallDefenderDefinitions" });

        // --- Services ---
        yield return Def(
            "service.inspect",
            "Inspect Service",
            "Services",
            "Selected Windows service",
            "Read service configuration, dependencies and failure history.",
            "A service report.",
            ActionRisk.Safe,
            new[] { "None" },
            "Standard user",
            "Report generated.",
            "None — read only.",
            new[] { "InspectService" });

        yield return Def(
            "service.set-manual",
            "Set Service Startup to Manual",
            "Services",
            "Selected non-critical service",
            "Change a non-critical service start mode to manual (reversible).",
            "Service no longer starts automatically at boot.",
            ActionRisk.Medium,
            new[] { "Service is not on the critical-services deny list",
                    "Current configuration snapshotted", "User approval recorded" },
            "Administrator",
            "Service start type reads 'Manual'; system boots normally.",
            "Startup Manager restores the snapshotted start type.",
            new[] { "SnapshotService", "SetServiceStartType" });

        yield return Def(
            "service.restore-startup",
            "Restore Service Startup",
            "Services",
            "Selected service",
            "Restore a service's original startup type from a snapshot.",
            "Original start type active again.",
            ActionRisk.Low,
            new[] { "A snapshot exists for this service" },
            "Administrator",
            "Start type matches the snapshot.",
            "None — this IS the rollback.",
            new[] { "SnapshotService", "SetServiceStartType" });

        // --- Network ---
        yield return Def(
            "network.diagnose",
            "Network Diagnostics",
            "Network",
            "TCP/IP stack, DNS, adapters",
            "Read adapter state, DNS client state and TCP/IP config.",
            "A read-only network report.",
            ActionRisk.Safe,
            new[] { "None" },
            "Standard user",
            "Report generated.",
            "None — read only.",
            new[] { "InspectNetwork" });

        yield return Def(
            "network.flush-dns",
            "Flush DNS Resolver Cache",
            "Network",
            "DNS client cache",
            "Clear the local DNS resolver cache.",
            "Fresh DNS resolutions; fixes stale-entry connectivity issues.",
            ActionRisk.Low,
            new[] { "network.diagnose completed" },
            "Administrator",
            "Connectivity restored to target hosts.",
            "Cache repopulates automatically.",
            new[] { "FlushDns" });

        yield return Def(
            "network.reset-tcpip",
            "Reset TCP/IP Stack",
            "Network",
            "Winsock & TCP/IP",
            "Reset the Winsock catalog and TCP/IP stack to defaults.",
            "Network stack returns to a clean state.",
            ActionRisk.High,
            new[] { "network.diagnose completed", "User warned about VPN/client reconfiguration",
                    "Restore point created" },
            "Administrator",
            "Adapters obtain addresses; connectivity restored.",
            "Reinstall VPN/virtual adapters if they were bound to Winsock.",
            new[] { "ResetWinsock", "ResetTcpIp" });

        // --- Storage ---
        yield return Def(
            "storage.check-disk",
            "Online CHKDSK",
            "Storage",
            "Selected volume",
            "Run CHKDSK in read-only mode to verify filesystem integrity.",
            "A filesystem verification report.",
            ActionRisk.Low,
            new[] { "Volume accessible", "No exclusive lock on system volume" },
            "Administrator",
            "CHKDSK reports no logical errors (or lists them).",
            "None — read-only mode.",
            new[] { "ChkdskReadOnly" });

        yield return Def(
            "storage.cleanup-temp",
            "Targeted Temporary File Cleanup",
            "Storage",
            "Listed temp paths (user-approved)",
            "Delete ONLY the temp files listed with exact paths and sizes after confirmation.",
            "Reclaimed disk space from approved paths.",
            ActionRisk.Medium,
            new[] { "Exact file list shown to user", "User confirmed each path",
                    "Files not in use" },
            "Administrator",
            "Approved paths no longer contain the listed files.",
            "Deleted temp files do not restore; review the list before approving.",
            new[] { "DeletePathConfirmed" });

        // --- Startup ---
        yield return Def(
            "startup.disable",
            "Disable Startup Entry",
            "Startup",
            "Selected startup entry",
            "Disable a startup entry without deleting any application file.",
            "Entry no longer launches at logon.",
            ActionRisk.Low,
            new[] { "Entry is not system-critical",
                    "Original configuration exported" },
            "Standard user (HKCU) / Administrator (HKLM)",
            "Entry reads as disabled; application still installed and runnable.",
            "Startup Manager re-enables from the exported backup.",
            new[] { "SnapshotStartup", "SetStartupState" });

        yield return Def(
            "startup.enable",
            "Enable Startup Entry",
            "Startup",
            "Selected startup entry",
            "Re-enable a previously disabled startup entry.",
            "Entry launches at logon again.",
            ActionRisk.Low,
            new[] { "Entry exists and is currently disabled" },
            "Standard user (HKCU) / Administrator (HKLM)",
            "Entry reads as enabled.",
            "Disable again from the same manager.",
            new[] { "SetStartupState" });
    }

    private static RepairActionDefinition Def(
        string id, string name, string category, string target, string purpose,
        string expected, ActionRisk risk, string[] preconditions, string permissions,
        string verification, string rollback, string[] verbs)
        => new(Id: id, DisplayName: name, Category: category, Target: target,
            Purpose: purpose, ExpectedResult: expected, Risk: risk,
            Preconditions: preconditions, RequiredPermissions: permissions,
            Verification: verification, Rollback: rollback, AllowedVerbs: verbs);
}
