using System.Diagnostics;
using System.Management;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Domain.Results;
using DriveInfo = NOVA.FIX.Domain.Models.DriveInfo;

namespace NOVA.FIX.Diagnostics.Probes;

/// <summary>
/// Real hardware probing via CIM/WMI with per-probe isolation: every query is
/// wrapped so a single failure (missing class, access denied, absent sensor)
/// is reported independently and never aborts the scan.
/// </summary>
public sealed class HardwareProbe : IHardwareProbe
{
    private readonly IAppLogger _logger;

    public HardwareProbe(IAppLogger logger)
    {
        _logger = logger;
    }

    public Task<QueryResult<CpuInfo>> GetCpuAsync(CancellationToken ct = default)
        => RunAsync("cpu", async () =>
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, " +
                "MaxClockSpeed, SocketDesignation, Architecture, " +
                "VirtualizationFirmwareEnabled FROM Win32_Processor");
            using var enumerator = searcher.Get().GetEnumerator();
            if (!enumerator.MoveNext())
                throw new InvalidOperationException("No CPU reported by WMI.");
            var cpu = (ManagementBaseObject)enumerator.Current!;
            var arch = Convert.ToInt32(cpu["Architecture"] ?? -1) switch
            {
                0 => "x86",
                5 => "ARM",
                9 => "x64",
                12 => "ARM64",
                _ => "Unknown",
            };
            var maxClock = Convert.ToDouble(cpu["MaxClockSpeed"] ?? 0);
            return new CpuInfo(
                Name: cpu["Name"]?.ToString()?.Trim() ?? "Unknown CPU",
                Manufacturer: cpu["Manufacturer"]?.ToString()?.Trim() ?? "Unknown",
                Socket: cpu["SocketDesignation"]?.ToString(),
                MaxClockMHz: maxClock > 0 ? maxClock : null,
                PhysicalCores: Convert.ToInt32(cpu["NumberOfCores"] ?? 0),
                LogicalProcessors: Convert.ToInt32(cpu["NumberOfLogicalProcessors"] ?? 0),
                Architecture: arch,
                VirtualizationFirmwareEnabled: Convert.ToBoolean(
                    cpu["VirtualizationFirmwareEnabled"] ?? false));
        }, ct);

    public Task<QueryResult<MemoryInfo>> GetMemoryAsync(CancellationToken ct = default)
        => RunAsync("memory", async () =>
        {
            using var osSearcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory, " +
                "TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem");
            using var osEnum = osSearcher.Get().GetEnumerator();
            long totalKb = 0, freeKb = 0, totalVirtualKb = 0, freeVirtualKb = 0;
            if (osEnum.MoveNext())
            {
                var os = osEnum.Current!;
                totalKb = Convert.ToInt64(os["TotalVisibleMemorySize"] ?? 0);
                freeKb = Convert.ToInt64(os["FreePhysicalMemory"] ?? 0);
                totalVirtualKb = Convert.ToInt64(os["TotalVirtualMemorySize"] ?? 0);
                freeVirtualKb = Convert.ToInt64(os["FreeVirtualMemory"] ?? 0);
            }

            var modules = new List<MemoryModule>();
            try
            {
                using var memSearcher = new ManagementObjectSearcher(
                    "SELECT BankLabel, Manufacturer, PartNumber, Capacity, " +
                    "Speed, FormFactor FROM Win32_PhysicalMemory");
                foreach (ManagementBaseObject o in memSearcher.Get())
                {
                    modules.Add(new MemoryModule(
                        Bank: o["BankLabel"]?.ToString() ?? "?",
                        Manufacturer: o["Manufacturer"]?.ToString()?.Trim() ?? "?",
                        PartNumber: o["PartNumber"]?.ToString()?.Trim() ?? "?",
                        CapacityBytes: Convert.ToInt64(o["Capacity"] ?? 0),
                        SpeedMHz: Convert.ToDouble(o["Speed"] ?? 0),
                        FormFactor: o["FormFactor"] switch
                        {
                            8 => "DIMM",
                            12 => "SODIMM",
                            _ => "Unknown",
                        }));
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, "HardwareProbe",
                    "Memory module enumeration failed (aggregate still available).", ex);
            }

            return new MemoryInfo(
                TotalBytes: totalKb * 1024,
                AvailableBytes: freeKb * 1024,
                CommittedBytes: (totalVirtualKb - freeVirtualKb) * 1024,
                Modules: modules);
        }, ct);

    public Task<QueryResult<MotherboardInfo>> GetMotherboardAsync(CancellationToken ct = default)
        => RunAsync("motherboard", async () =>
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Product, SerialNumber, Version FROM Win32_BaseBoard");
            using var e = searcher.Get().GetEnumerator();
            if (!e.MoveNext()) throw new InvalidOperationException("No baseboard reported.");
            var b = e.Current!;
            return new MotherboardInfo(
                b["Manufacturer"]?.ToString() ?? "Unknown",
                b["Product"]?.ToString() ?? "Unknown",
                b["SerialNumber"]?.ToString()?.Trim() ?? string.Empty,
                b["Version"]?.ToString() ?? "Unknown");
        }, ct);

    public Task<QueryResult<BiosInfo>> GetBiosAsync(CancellationToken ct = default)
        => RunAsync("bios", async () =>
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            using var e = searcher.Get().GetEnumerator();
            if (!e.MoveNext()) throw new InvalidOperationException("No BIOS reported.");
            var b = e.Current!;
            DateTime? released = null;
            var raw = b["ReleaseDate"];
            if (raw is not null)
            {
                try
                {
                    released = ManagementDateTimeConverter.ToDateTime(raw.ToString()!);
                }
                catch
                {
                    released = null;
                }
            }
            return new BiosInfo(
                b["Manufacturer"]?.ToString() ?? "Unknown",
                b["SMBIOSBIOSVersion"]?.ToString() ?? "Unknown",
                released,
                SecureBootCapable: false); // refined by GetSecureBootAsync
        }, ct);

    public Task<QueryResult<IReadOnlyList<GpuDeviceInfo>>> GetGpusAsync(CancellationToken ct = default)
        => RunAsync("gpus", async () =>
        {
            var gpus = new List<GpuDeviceInfo>();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                var pnp = o["PNPDeviceID"]?.ToString() ?? string.Empty;
                gpus.Add(new GpuDeviceInfo(
                    Name: o["Name"]?.ToString() ?? "Unknown GPU",
                    AdapterRam: o["AdapterRAM"]?.ToString() ?? "0",
                    DriverVersion: o["DriverVersion"]?.ToString() ?? "Unknown",
                    DriverDate: o["DriverDate"]?.ToString() ?? "Unknown",
                    PnpDeviceId: pnp,
                    IsDiscrete: !pnp.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase)));
            }
            if (gpus.Count == 0)
                throw new InvalidOperationException("No video controller reported.");
            return (IReadOnlyList<GpuDeviceInfo>)gpus;
        }, ct);

    public Task<QueryResult<IReadOnlyList<DriveInfo>>> GetDrivesAsync(CancellationToken ct = default)
        => RunAsync("drives", async () =>
        {
            var drives = new List<DriveInfo>();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Model, InterfaceType, MediaType, Size, SerialNumber, " +
                "FirmwareRevision FROM Win32_DiskDrive");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                drives.Add(new DriveInfo(
                    Model: o["Model"]?.ToString()?.Trim() ?? "Unknown",
                    InterfaceType: o["InterfaceType"]?.ToString() ?? "Unknown",
                    MediaType: o["MediaType"]?.ToString() ?? "Unknown",
                    SizeBytes: Convert.ToInt64(o["Size"] ?? 0),
                    SerialNumber: o["SerialNumber"]?.ToString()?.Trim(),
                    Firmware: o["FirmwareRevision"]?.ToString()?.Trim(),
                    SmartAttributes: Array.Empty<SmartAttribute>()));
            }
            if (drives.Count == 0)
                throw new InvalidOperationException("No physical disk reported.");
            return (IReadOnlyList<DriveInfo>)drives;
        }, ct);

    public Task<QueryResult<IReadOnlyList<VolumeInfo>>> GetVolumesAsync(CancellationToken ct = default)
        => RunAsync("volumes", async () =>
        {
            var volumes = new List<VolumeInfo>();
            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                try
                {
                    volumes.Add(new VolumeInfo(
                        Mount: drive.Name,
                        Label: drive.VolumeLabel,
                        FileSystem: drive.DriveFormat,
                        SizeBytes: drive.TotalSize,
                        FreeBytes: drive.TotalFreeSpace,
                        DriveType: drive.DriveType.ToString()));
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Warn, "HardwareProbe",
                        $"Volume {drive.Name} unreadable.", ex);
                }
            }
            return (IReadOnlyList<VolumeInfo>)volumes;
        }, ct);

    public Task<QueryResult<IReadOnlyList<DeviceEntry>>> GetDevicesAsync(CancellationToken ct = default)
        => RunAsync("devices", async () =>
        {
            var devices = new List<DeviceEntry>();
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPClass, Manufacturer, ConfigManagerErrorCode, " +
                "PNPDeviceID FROM Win32_PnPEntity");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                var code = Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0);
                devices.Add(new DeviceEntry(
                    Name: o["Name"]?.ToString() ?? "Unknown device",
                    Class: o["PNPClass"]?.ToString(),
                    Manufacturer: o["Manufacturer"]?.ToString(),
                    Status: code == 0 ? "OK" : $"Error {code}",
                    ProblemCode: code == 0 ? null : code.ToString(),
                    PnpDeviceId: o["PNPDeviceID"]?.ToString() ?? string.Empty));
            }
            return (IReadOnlyList<DeviceEntry>)devices;
        }, ct);

    public async Task<QueryResult<string?>> GetTpmStatusAsync(CancellationToken ct = default)
    {
        // Root\cimv2\security\microsofttpm requires elevation; degrade honestly.
        var result = await RunAsync<string?>("tpm", async () =>
        {
            using var searcher = new ManagementObjectSearcher(
                @"ROOT\CIMV2\Security\MicrosoftTpm",
                "SELECT IsEnabled_InitialValue, ManufacturerIdTxt FROM Win32_Tpm");
            using var e = searcher.Get().GetEnumerator();
            if (!e.MoveNext()) return null;
            var tpm = e.Current!;
            var enabled = Convert.ToBoolean(tpm["IsEnabled_InitialValue"] ?? false);
            var mfr = tpm["ManufacturerIdTxt"]?.ToString();
            return $"TPM {(enabled ? "enabled" : "disabled")}" +
                   (string.IsNullOrWhiteSpace(mfr) ? string.Empty : $" ({mfr})");
        }, ct);

        if (!result.Success && result.Error?.Contains("Invalid class", StringComparison.OrdinalIgnoreCase) == true)
        {
            return QueryResult<string?>.Ok("tpm", "TPM not present on this system.", method: "WMI");
        }
        return result;
    }

    public Task<QueryResult<bool?>> GetSecureBootAsync(CancellationToken ct = default)
        => RunAsync<bool?>("secureboot", async () =>
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SecureBoot FROM Win32_ComputerSystem");
            using var e = searcher.Get().GetEnumerator();
            if (!e.MoveNext()) return null;
            return Convert.ToBoolean(e.Current!["SecureBoot"] ?? false);
        }, ct);

    public Task<QueryResult<string?>> GetBatteryStatusAsync(CancellationToken ct = default)
        => RunAsync<string?>("battery", async () =>
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
            using var e = searcher.Get().GetEnumerator();
            if (!e.MoveNext()) return "No battery (desktop system).";
            var b = e.Current!;
            var pct = b["EstimatedChargeRemaining"];
            return pct is null
                ? "Battery present (charge unknown)."
                : $"Battery {pct}% (status {b["BatteryStatus"]})";
        }, ct);

    private async Task<QueryResult<T>> RunAsync<T>(
        string probeName, Func<Task<T>> probe, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            var value = await Task.Run(probe, ct).ConfigureAwait(false);
            sw.Stop();
            return QueryResult<T>.Ok(probeName, value, method: "WMI/CIM", duration: sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Log(LogLevel.Warn, "HardwareProbe", $"Probe '{probeName}' failed.", ex);
            return QueryResult<T>.Fail(probeName, ex.Message, method: "WMI/CIM", duration: sw.Elapsed);
        }
    }
}
