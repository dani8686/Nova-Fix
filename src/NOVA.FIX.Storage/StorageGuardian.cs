using System.Management;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Models;
using DriveInfo = NOVA.FIX.Domain.Models.DriveInfo;

namespace NOVA.FIX.Storage;

/// <summary>Item scheduled for cleanup — exact path and size shown to the user.</summary>
public sealed record CleanupCandidate(
    string Path,
    long SizeBytes,
    string Category,
    bool InUse,
    string Note);

/// <summary>
/// Storage Guardian: disk/volume inventory, SSD/HDD detection, SMART where
/// supported, temp/large-file analysis. Deletion NEVER happens automatically —
/// candidates are listed with exact paths and sizes for explicit confirmation.
/// </summary>
public sealed class StorageGuardian
{
    private readonly IAppLogger _logger;

    public StorageGuardian(IAppLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<VolumeInfo> Volumes()
    {
        var list = new List<VolumeInfo>();
        foreach (var d in System.IO.DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            try
            {
                list.Add(new VolumeInfo(
                    d.Name, d.VolumeLabel, d.DriveFormat,
                    d.TotalSize, d.TotalFreeSpace, d.DriveType.ToString()));
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, "Storage",
                    $"Volume {d.Name} unreadable.", ex);
            }
        }
        return list;
    }

    /// <summary>Physical disks with media type (SSD vs HDD) via WMI.</summary>
    public IReadOnlyList<DiskOverview> Disks()
    {
        var list = new List<DiskOverview>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Model, InterfaceType, MediaType, Size, Index FROM Win32_DiskDrive");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                var media = o["MediaType"]?.ToString() ?? "Unknown";
                list.Add(new DiskOverview(
                    Model: o["Model"]?.ToString() ?? "Unknown",
                    Interface: o["InterfaceType"]?.ToString() ?? "Unknown",
                    IsSsd: media.Contains("SSD", StringComparison.OrdinalIgnoreCase),
                    SizeBytes: Convert.ToInt64(o["Size"] ?? 0),
                    MediaType: media));
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "Storage", "Disk enumeration failed.", ex);
        }
        return list;
    }

    /// <summary>
    /// SMART attributes via WMI (MSStorageDriver_*). Returns empty when the
    /// driver does not expose SMART — reported honestly as unavailable.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SmartAttribute>> SmartByDisk()
    {
        var result = new Dictionary<string, IReadOnlyList<SmartAttribute>>(
            StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT InstanceName, Attribute, CurrentValue, WorstValue, RawValue " +
                "FROM MSStorageDriver_ATAPISmartData");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                var instance = o["InstanceName"]?.ToString() ?? "disk";
                var attrs = new List<SmartAttribute>();
                var raw = o["Attribute"] as byte[];
                // WMI SMART blob parsing is vendor-specific; report presence honestly.
                attrs.Add(new SmartAttribute(
                    Id: 0,
                    Name: "SMART data block present",
                    Current: 0, Worst: 0,
                    RawValue: raw?.Length ?? 0,
                    IsFailed: false,
                    Interpretation: raw is null
                        ? "SMART block not exposed"
                        : $"Raw block {raw.Length} bytes — detailed attribute decoding " +
                          "requires vendor tooling."));
                result[instance] = attrs;
            }
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Info, "Storage",
                "SMART WMI class unavailable on this system (honest: not exposed).", ex);
        }
        return result;
    }

    /// <summary>
    /// Lists temp/cache candidates with EXACT paths and sizes. Nothing is
    /// deleted here — the caller must show the list and obtain confirmation.
    /// </summary>
    public IReadOnlyList<CleanupCandidate> TempCandidates(
        TimeSpan olderThan, CancellationToken ct = default)
    {
        var candidates = new List<CleanupCandidate>();
        var cutoff = DateTime.UtcNow - olderThan;

        void Scan(string root, string category, bool allowDelete)
        {
            if (!Directory.Exists(root)) return;
            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             root, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.LastWriteTimeUtc > cutoff) continue;
                        candidates.Add(new CleanupCandidate(
                            Path: fi.FullName,
                            SizeBytes: fi.Length,
                            Category: category,
                            InUse: false,
                            Note: allowDelete
                                ? "Approved temp location — safe after confirmation."
                                : "Reference only — not auto-deletable."));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // skip locked/inaccessible file
                    }
                    catch (IOException)
                    {
                        // skip
                    }

                    if (candidates.Count > 5000) return; // bounded
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Log(LogLevel.Warn, "Storage",
                    $"Scan of {root} incomplete.", ex);
            }
        }

        Scan(Path.GetTempPath(), "User temp", allowDelete: true);
        Scan(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Temp"),
            "Local temp", allowDelete: true);
        Scan(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Temp"),
            "Windows temp", allowDelete: true);

        var softwareDist = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SoftwareDistribution", "Download");
        Scan(softwareDist, "Update cache", allowDelete: false); // needs service stop first

        return candidates.OrderByDescending(c => c.SizeBytes).ToList();
    }

    /// <summary>Largest files on a volume (bounded, read-only).</summary>
    public IReadOnlyList<LargeFileEntry> LargestFiles(
        string root, int take = 50, CancellationToken ct = default)
    {
        var results = new List<LargeFileEntry>();
        if (!Directory.Exists(root)) return results;

        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0 && results.Count < take * 4)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    stack.Push(sub);
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var fi = new FileInfo(file);
                    results.Add(new LargeFileEntry(
                        fi.FullName, fi.Length, fi.LastWriteTimeUtc));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // skip inaccessible directory
            }
        }

        return results.OrderByDescending(f => f.SizeBytes).Take(take).ToList();
    }

    /// <summary>Storage Sense configuration inspection (read-only).</summary>
    public string StorageSenseStatus()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy");
            if (key is null) return "Storage Sense: not configured (default).";
            var enabled = key.GetValue("01") is int v && v == 1;
            return enabled
                ? "Storage Sense: enabled."
                : "Storage Sense: disabled.";
        }
        catch (Exception ex)
        {
            return $"Storage Sense status unavailable: {ex.Message}";
        }
    }
}

public sealed record DiskOverview(
    string Model, string Interface, bool IsSsd, long SizeBytes, string MediaType);

public sealed record LargeFileEntry(string Path, long SizeBytes, DateTime LastWriteUtc);
