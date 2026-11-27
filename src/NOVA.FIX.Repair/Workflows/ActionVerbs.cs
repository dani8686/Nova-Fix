using System.Diagnostics;
using System.ServiceProcess;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Results;
using NOVA.FIX.Infrastructure.Execution;

namespace NOVA.FIX.Repair.Workflows;

/// <summary>
/// Concrete, validated implementations of allowlisted verbs. Each verb is a
/// fixed operation — parameters only fill known slots (service names, approved
/// paths). There is no path from AI text to command construction.
/// </summary>
public sealed class ActionVerbs
{
    private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "DcomLaunch", "RpcEptMapper", "LanmanWorkstation", "LanmanServer",
        "Dnscache", "Dhcp", "NlaSvc", "EventLog", "PlugPlay", "Power", "SamSs",
        "LSM", "SecurityHealthService", "WinDefend", "WdNisSvc", "Sense",
        "CryptSvc", "BFE", "MpsSvc", "Tcpip", "nsi", "Schedule", "ProfSvc",
        "UserManager", "ShellHWDetection", "AudioSrv", "Themes", "Spooler",
    };

    private readonly ProcessRunner _runner;
    private readonly IAppLogger _logger;

    public ActionVerbs(ProcessRunner runner, IAppLogger logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public bool IsCriticalService(string serviceName)
        => CriticalServices.Contains(serviceName);

    // ---------- DISM / SFC ----------

    public Task<ProcessRunResult> DismCheckHealth(CancellationToken ct)
        => _runner.RunAsync("DISM.exe",
            new[] { "/Online", "/Cleanup-Image", "/CheckHealth" },
            TimeSpan.FromMinutes(15), ct);

    public Task<ProcessRunResult> DismScanHealth(CancellationToken ct)
        => _runner.RunAsync("DISM.exe",
            new[] { "/Online", "/Cleanup-Image", "/ScanHealth" },
            TimeSpan.FromMinutes(30), ct);

    public Task<ProcessRunResult> DismRestoreHealth(CancellationToken ct)
        => _runner.RunAsync("DISM.exe",
            new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" },
            TimeSpan.FromMinutes(45), ct);

    public Task<ProcessRunResult> SfcScanNow(CancellationToken ct)
        => _runner.RunAsync("sfc.exe", new[] { "/scannow" },
            TimeSpan.FromMinutes(30), ct);

    // ---------- Services ----------

    public Task<ProcessRunResult> InspectService(string serviceName, CancellationToken ct)
        => _runner.RunAsync("sc.exe",
            new[] { "query", serviceName }, TimeSpan.FromSeconds(30), ct);

    public async Task<OperationResult> StopServiceSafe(
        string serviceName, CancellationToken ct)
    {
        if (IsCriticalService(serviceName))
        {
            return new OperationResult
            {
                ActionId = "StopService",
                Status = Domain.Enums.OperationStatus.Skipped,
                Error = $"Service '{serviceName}' is on the critical-services deny list. " +
                        "NOVA FIX never stops critical services automatically.",
            };
        }

        var r = await _runner.RunAsync("sc.exe",
            new[] { "stop", serviceName }, TimeSpan.FromSeconds(45), ct)
            .ConfigureAwait(false);
        return ToOperation("StopService", r);
    }

    public async Task<OperationResult> StartServiceSafe(
        string serviceName, CancellationToken ct)
    {
        var r = await _runner.RunAsync("sc.exe",
            new[] { "start", serviceName }, TimeSpan.FromSeconds(45), ct)
            .ConfigureAwait(false);
        return ToOperation("StartService", r);
    }

    public async Task<OperationResult> SetServiceStartType(
        string serviceName, string startType, CancellationToken ct)
    {
        if (IsCriticalService(serviceName))
        {
            return new OperationResult
            {
                ActionId = "SetServiceStartType",
                Status = Domain.Enums.OperationStatus.Skipped,
                Error = $"Service '{serviceName}' is critical; modification denied.",
            };
        }

        var arg = startType.ToLowerInvariant() switch
        {
            "automatic" => "auto",
            "manual" => "demand",
            "disabled" => "disabled",
            _ => null,
        };
        if (arg is null)
        {
            return new OperationResult
            {
                ActionId = "SetServiceStartType",
                Status = Domain.Enums.OperationStatus.Failed,
                Error = $"Invalid start type '{startType}'.",
            };
        }

        var r = await _runner.RunAsync("sc.exe",
            new[] { "config", serviceName, "start=", arg },
            TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        return ToOperation("SetServiceStartType", r);
    }

    public (string Status, string StartType, string Path) ReadService(
        string serviceName)
    {
        using var sc = new ServiceController(serviceName);
        return (sc.Status.ToString(), sc.StartType.ToString(),
            sc.CanPauseAndContinue ? string.Empty : string.Empty);
    }

    // ---------- Network ----------

    public Task<ProcessRunResult> FlushDns(CancellationToken ct)
        => _runner.RunAsync("ipconfig.exe", new[] { "/flushdns" },
            TimeSpan.FromSeconds(30), ct);

    public Task<ProcessRunResult> ResetWinsock(CancellationToken ct)
        => _runner.RunAsync("netsh.exe", new[] { "winsock", "reset" },
            TimeSpan.FromMinutes(2), ct);

    public Task<ProcessRunResult> ResetTcpIp(CancellationToken ct)
        => _runner.RunAsync("netsh.exe", new[] { "int", "ip", "reset" },
            TimeSpan.FromMinutes(2), ct);

    // ---------- Storage ----------

    public Task<ProcessRunResult> ChkdskReadOnly(
        string mount, CancellationToken ct)
    {
        var drive = mount.TrimEnd('\\', '/').TrimEnd(':');
        if (drive.Length != 1 || !char.IsLetter(drive[0]))
        {
            return Task.FromResult(new ProcessRunResult(-1, string.Empty,
                $"Invalid volume '{mount}'.", TimeSpan.Zero, false, true));
        }
        return _runner.RunAsync("chkdsk.exe",
            new[] { $"{drive}:", "/scan" }, TimeSpan.FromMinutes(20), ct);
    }

    /// <summary>
    /// Deletes ONLY paths that were explicitly listed to and confirmed by the
    /// user (the confirmation flag is passed from the UI layer).
    /// </summary>
    public async Task<OperationResult> DeletePathConfirmed(
        string fullPath, bool userConfirmed, CancellationToken ct)
    {
        if (!userConfirmed)
        {
            return new OperationResult
            {
                ActionId = "DeletePathConfirmed",
                Status = Domain.Enums.OperationStatus.Skipped,
                Error = "User confirmation not granted — nothing was deleted.",
            };
        }

        var (ok, error) = ValidateDeletionTarget(fullPath);
        if (!ok)
        {
            return new OperationResult
            {
                ActionId = "DeletePathConfirmed",
                Status = Domain.Enums.OperationStatus.Failed,
                Error = error!,
            };
        }

        var sw = Stopwatch.StartNew();
        try
        {
            long freed = 0;
            if (File.Exists(fullPath))
            {
                freed = new FileInfo(fullPath).Length;
                File.Delete(fullPath);
            }
            else if (Directory.Exists(fullPath))
            {
                freed = new DirectoryInfo(fullPath)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
                Directory.Delete(fullPath, recursive: true);
            }
            else
            {
                return new OperationResult
                {
                    ActionId = "DeletePathConfirmed",
                    Status = Domain.Enums.OperationStatus.Skipped,
                    Output = "Path does not exist (already clean).",
                    Duration = sw.Elapsed,
                };
            }

            _logger.Log(LogLevel.Info, "Repair",
                $"Deleted confirmed target: {fullPath} ({freed} bytes)");
            return new OperationResult
            {
                ActionId = "DeletePathConfirmed",
                Status = Domain.Enums.OperationStatus.Succeeded,
                Output = $"Removed {fullPath} ({freed / 1024.0:0.0} KB).",
                Duration = sw.Elapsed,
            };
        }
        catch (Exception ex)
        {
            return new OperationResult
            {
                ActionId = "DeletePathConfirmed",
                Status = Domain.Enums.OperationStatus.Failed,
                Error = ex.Message,
                Duration = sw.Elapsed,
            };
        }
    }

    /// <summary>
    /// Validation gate for destructive paths: never allow system roots, user
    /// profile roots, Windows directory, Program Files, or anything outside
    /// known temp/cache locations.
    /// </summary>
    internal static (bool Ok, string? Error) ValidateDeletionTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "Empty path.");

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            return (false, $"Unresolvable path: {ex.Message}");
        }

        var normalized = full.TrimEnd('\\', '/');
        var forbidden = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.GetPathRoot(normalized) ?? string.Empty,
        };

        foreach (var f in forbidden)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            var fe = f.TrimEnd('\\', '/');
            if (string.Equals(normalized, fe, StringComparison.OrdinalIgnoreCase))
                return (false, $"Refusing to delete protected root '{full}'.");
        }

        // Only temp/cache-style locations are auto-approved for deletion.
        var tempRoots = new[]
        {
            Path.GetTempPath(),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Temp"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Temp"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "SoftwareDistribution", "Download"),
        };

        var allowed = tempRoots.Any(t =>
        {
            var te = Path.GetFullPath(t).TrimEnd('\\', '/');
            return normalized.StartsWith(te + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, te,
                       StringComparison.OrdinalIgnoreCase);
        });

        return allowed
            ? (true, null)
            : (false, $"Path '{full}' is outside approved temp/cache locations.");
    }

    // ---------- Defender ----------

    public Task<ProcessRunResult> InspectDefender(CancellationToken ct)
        => _runner.RunAsync("powershell.exe",
            new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                "Get-MpComputerStatus | Select-Object AMServiceEnabled," +
                "AntivirusEnabled,RealTimeProtectionEnabled," +
                "AntivirusSignatureVersion,AntisignatureLastUpdated | Format-List",
            },
            TimeSpan.FromMinutes(2), ct);

    public Task<ProcessRunResult> InstallDefenderDefinitions(
        string packagePath, CancellationToken ct)
        => _runner.RunAsync("powershell.exe",
            new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                $"Update-MpSignature -Path '{packagePath.Replace("'", "''")}'",
            },
            TimeSpan.FromMinutes(10), ct);

    // ---------- Startup ----------

    public Task<ProcessRunResult> InspectNetwork(CancellationToken ct)
        => _runner.RunAsync("powershell.exe",
            new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                "Get-NetIPConfiguration | Format-List; ipconfig /all",
            },
            TimeSpan.FromMinutes(2), ct);

    private static OperationResult ToOperation(string verb, ProcessRunResult r)
        => new()
        {
            ActionId = verb,
            Status = !r.Launched ? Domain.Enums.OperationStatus.Failed
                : r.TimedOut ? Domain.Enums.OperationStatus.Failed
                : r.ExitCode == 0 ? Domain.Enums.OperationStatus.Succeeded
                : Domain.Enums.OperationStatus.Failed,
            ExitCode = r.ExitCode,
            Output = r.StandardOutput,
            Error = r.StandardError,
            Duration = r.Duration,
        };
}
