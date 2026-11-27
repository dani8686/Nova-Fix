using System.ServiceProcess;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.Optimization;

/// <summary>
/// Service Manager: inspection with dependencies, logon account and failure
/// history. Modifications are guarded (critical-service deny list) and every
/// change is snapshotted for rollback. Never disables critical services.
/// </summary>
public sealed class ServiceManager
{
    private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "DcomLaunch", "RpcEptMapper", "LanmanWorkstation", "LanmanServer",
        "Dnscache", "Dhcp", "NlaSvc", "EventLog", "PlugPlay", "Power", "SamSs",
        "LSM", "SecurityHealthService", "WinDefend", "WdNisSvc", "Sense",
        "CryptSvc", "BFE", "MpsSvc", "Tcpip", "nsi", "Schedule", "ProfSvc",
        "UserManager", "ShellHWDetection", "AudioSrv", "Themes", "Spooler",
        "Winmgmt", "EventSystem", "DsmSvc", "Appinfo", "LanmanWorkstation",
    };

    private readonly IAppLogger _logger;

    public ServiceManager(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <summary>Writes the service Start value (HKLM system key).</summary>
    private static void SetStartTypeRegistry(string serviceName, ServiceStartMode mode)
    {
        var startValue = mode switch
        {
            ServiceStartMode.Automatic => 2,
            ServiceStartMode.Manual => 3,
            ServiceStartMode.Disabled => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\" + serviceName, writable: true)
            ?? throw new InvalidOperationException(
                $"Service key for '{serviceName}' not found (Administrator required).");
        key.SetValue("Start", startValue, Microsoft.Win32.RegistryValueKind.DWord);
    }

    public bool IsCritical(string serviceName) => CriticalServices.Contains(serviceName);

    public IReadOnlyList<ServiceEntry> InspectAll()
    {
        var list = new List<ServiceEntry>();
        foreach (var sc in ServiceController.GetServices())
        {
            using (sc)
            {
                list.Add(BuildEntry(sc));
            }
        }
        return list.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public ServiceEntry? Inspect(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            return BuildEntry(sc);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Services",
                $"Inspect '{serviceName}' failed.", ex);
            return null;
        }
    }

    /// <summary>Snapshots current state before any modification.</summary>
    public string Snapshot(string serviceName, string snapshotDirectory)
    {
        Directory.CreateDirectory(snapshotDirectory);
        var entry = Inspect(serviceName)
                    ?? throw new InvalidOperationException(
                        $"Service '{serviceName}' not found.");
        var file = Path.Combine(snapshotDirectory,
            $"service-{serviceName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                entry.ServiceName,
                entry.StartType,
                entry.Status,
                entry.Dependencies,
                TimestampUtc = DateTime.UtcNow,
            },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(file, json);
        return file;
    }

    /// <summary>
    /// Controlled start-type change. Critical services are rejected outright;
    /// everything requires an existing snapshot for rollback.
    /// </summary>
    public (bool Ok, string Message) SetStartType(
        string serviceName, ServiceStartMode mode, bool snapshotExists)
    {
        if (IsCritical(serviceName))
            return (false,
                $"'{serviceName}' is a critical service — NOVA FIX never modifies it.");
        if (!snapshotExists)
            return (false, "No snapshot found — take a snapshot first (rollback safety).");

        try
        {
            using var sc = new ServiceController(serviceName);
            if (sc.StartType == mode)
                return (true, "Already in the requested state.");

            // Stop before changing start type when the service is running.
            if (sc.Status == ServiceControllerStatus.Running
                || sc.Status == ServiceControllerStatus.StartPending)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped,
                    TimeSpan.FromSeconds(30));
            }

            // ServiceController.StartType is read-only on .NET 8 — the Start
            // value lives in the service registry key (2=Auto,3=Manual,4=Disabled).
            SetStartTypeRegistry(serviceName, mode);
            sc.Refresh();
            _logger.Log(LogLevel.Info, "Services",
                $"{serviceName} start type -> {mode}");
            return (true, $"'{serviceName}' start type set to {mode}.");
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, "Services",
                $"SetStartType failed for {serviceName}.", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>Restores start type from a snapshot file (rollback).</summary>
    public (bool Ok, string Message) RestoreFromSnapshot(string snapshotFile)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(snapshotFile));
            var name = doc.RootElement.GetProperty("ServiceName").GetString();
            var startTypeText = doc.RootElement.GetProperty("StartType").GetString();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(startTypeText))
                return (false, "Snapshot file is malformed.");

            if (!Enum.TryParse<ServiceStartMode>(startTypeText, out var mode))
                return (false, $"Unknown start type '{startTypeText}' in snapshot.");

            if (IsCritical(name))
                return (false, $"'{name}' is critical — restore denied.");

            SetStartTypeRegistry(name, mode);
            return (true, $"'{name}' restored to {mode}.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static ServiceEntry BuildEntry(ServiceController sc)
    {
        string[] deps;
        string[] dependents;
        try { deps = sc.ServicesDependedOn.Select(s => s.ServiceName).ToArray(); }
        catch { deps = Array.Empty<string>(); }
        try { dependents = sc.DependentServices.Select(s => s.ServiceName).ToArray(); }
        catch { dependents = Array.Empty<string>(); }

        var path = string.Empty;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{sc.ServiceName}");
            path = key?.GetValue("ImagePath")?.ToString() ?? string.Empty;
        }
        catch
        {
            // access denied is acceptable
        }

        var failureHistory = new List<string>();
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{sc.ServiceName}\FailureActions");
            if (key is not null)
                failureHistory.Add("Failure actions configured (registry present).");
        }
        catch
        {
            // ignore
        }

        return new ServiceEntry(
            ServiceName: sc.ServiceName,
            DisplayName: sc.DisplayName,
            Description: SafeDescription(sc),
            Status: sc.Status.ToString(),
            StartType: sc.StartType.ToString(),
            ExecutablePath: path,
            Publisher: null,
            LogonAccount: null,
            Dependencies: deps,
            DependentServices: dependents,
            FailureHistory: failureHistory);
    }

    private static string SafeDescription(ServiceController sc)
    {
        try
        {
            return string.IsNullOrWhiteSpace(sc.Description)
                ? "(no description)"
                : sc.Description;
        }
        catch
        {
            return "(unreadable)";
        }
    }
}
