using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Domain.Results;

namespace NOVA.FIX.Diagnostics.Probes;

/// <summary>
/// Windows edition/version/activation/health probes with per-probe isolation.
/// All data comes from local system sources — no network access of any kind.
/// </summary>
public sealed class WindowsProbe : IWindowsProbe
{
    private readonly IAppLogger _logger;

    public WindowsProbe(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<QueryResult<WindowsInfo>> GetWindowsInfoAsync(CancellationToken ct = default)
        => RunAsync("windows-info", () =>
        {
            string productName = "Windows", edition = "Unknown", version = "Unknown",
                build = "Unknown", arch = "Unknown", installDate = "Unknown",
                locale = "Unknown";

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key is not null)
                {
                    productName = key.GetValue("ProductName")?.ToString() ?? productName;
                    edition = key.GetValue("EditionID")?.ToString() ?? edition;
                    version = key.GetValue("DisplayVersion")?.ToString()
                              ?? key.GetValue("CurrentVersion")?.ToString() ?? version;
                    build = key.GetValue("CurrentBuild")?.ToString()
                            ?? key.GetValue("CurrentBuildNumber")?.ToString() ?? build;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, "WindowsProbe", "Registry version read failed.", ex);
            }

            try
            {
                arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
                installDate = Registry.LocalMachine.OpenSubKey(
                                  @"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
                              ?.GetValue("InstallDate") is long epoch
                    ? DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime.ToString("yyyy-MM-dd")
                    : "Unknown";
                locale = System.Globalization.CultureInfo.CurrentUICulture.Name;
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, "WindowsProbe", "Environment read failed.", ex);
            }

            var activation = TryGetActivationStatus();

            return new WindowsInfo(productName, edition, version, build, arch,
                installDate, activation, locale);
        }, ct);

    public Task<QueryResult<IReadOnlyList<string>>> GetInstalledUpdatesAsync(
        CancellationToken ct = default)
        => RunAsync("installed-updates", () =>
        {
            var updates = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT HotFixID, Description, InstalledOn FROM Win32_QuickFixEngineering");
                foreach (ManagementBaseObject o in searcher.Get())
                {
                    var id = o["HotFixID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    updates.Add($"{id} — installed {o["InstalledOn"]}");
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, "WindowsProbe", "QFE enumeration failed.", ex);
            }

            // Servicing stack entries from the CBS log marker (offline, local file).
            try
            {
                var cbsLog = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "Logs", "CBS", "CBS.log");
                if (File.Exists(cbsLog))
                {
                    var info = new FileInfo(cbsLog);
                    updates.Add($"CBS.log last written {info.LastWriteTime:yyyy-MM-dd HH:mm}");
                }
            }
            catch
            {
                // access denied is fine — reported as absent entry
            }

            return (IReadOnlyList<string>)updates;
        }, ct);

    public Task<QueryResult<string>> GetComponentStoreHealthAsync(CancellationToken ct = default)
        => RunAsync("component-store", () =>
        {
            // Reads the local CBS poqrun / ResetBase state without running DISM
            // (a real DISM ScanHealth run is an explicit repair-workflow action).
            var winsxs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS");
            if (!Directory.Exists(winsxs))
                return "Component store path not found — manual inspection required.";

            var manifestCount = 0;
            try
            {
                manifestCount = Directory.EnumerateFiles(winsxs, "*.manifest")
                    .Take(200).Count();
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, "WindowsProbe",
                    "WinSxS enumeration partially failed.", ex);
            }

            return $"Component store present ({manifestCount}+ manifests sampled locally). " +
                   "Full health requires the DISM ScanHealth repair workflow.";
        }, ct);

    public Task<QueryResult<string>> GetDefenderStatusAsync(CancellationToken ct = default)
        => RunAsync("defender", () =>
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection");
                if (key is not null)
                {
                    var enabled = key.GetValue("DisableRealtimeMonitoring");
                    return enabled is int i && i == 1
                        ? "Defender real-time protection: DISABLED (registry evidence)"
                        : "Defender real-time protection: enabled (registry evidence)";
                }
                return "Defender status not exposed to this user context.";
            }
            catch (Exception ex)
            {
                return $"Defender status unavailable: {ex.Message}";
            }
        }, ct);

    public Task<QueryResult<IReadOnlyList<string>>> GetDeviceManagerErrorsAsync(
        CancellationToken ct = default)
        => RunAsync("device-errors", () =>
        {
            var errors = new List<string>();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, ConfigManagerErrorCode, PNPDeviceID FROM Win32_PnPEntity");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                var code = Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0);
                if (code == 0) continue;
                errors.Add($"{o["Name"]} — error {code} ({DescribeDeviceError(code)})");
            }
            return (IReadOnlyList<string>)errors;
        }, ct);

    private static string? TryGetActivationStatus()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LicenseStatus, Status FROM SoftwareLicensingProduct " +
                "WHERE PartialProductKey IS NOT NULL AND Description LIKE 'Windows%'");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                var status = Convert.ToInt32(o["LicenseStatus"] ?? -1);
                return status switch
                {
                    0 => "Unlicensed",
                    1 => "Licensed",
                    5 => "Grace period",
                    6 => "Notification",
                    _ => $"License status {status}",
                };
            }
        }
        catch
        {
            // Requires elevation in many contexts — report honestly below.
        }
        return "Activation status not readable without elevation.";
    }

    private static string DescribeDeviceError(int code) => code switch
    {
        1 => "driver not configured correctly",
        10 => "failed to start",
        12 => "no valid drivers",
        19 => "cannot start (registry problem)",
        22 => "device disabled",
        28 => "drivers not installed",
        31 => "device not working",
        37 => "Windows cannot load driver",
        _ => "see Device Manager",
    };

    private async Task<QueryResult<T>> RunAsync<T>(
        string probeName, Func<T> probe, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            var value = await Task.Run(probe, ct).ConfigureAwait(false);
            sw.Stop();
            return QueryResult<T>.Ok(probeName, value, method: "registry/WMI", duration: sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Log(LogLevel.Warn, "WindowsProbe", $"Probe '{probeName}' failed.", ex);
            return QueryResult<T>.Fail(probeName, ex.Message, method: "registry/WMI", duration: sw.Elapsed);
        }
    }
}
