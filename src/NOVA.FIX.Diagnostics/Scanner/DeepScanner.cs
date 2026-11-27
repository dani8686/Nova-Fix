using System.Text.Json;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Domain.Results;
using DriveInfo = NOVA.FIX.Domain.Models.DriveInfo;

namespace NOVA.FIX.Diagnostics.Scanner;

/// <summary>Result of a complete deep scan: every probe outcome kept independent.</summary>
public sealed record DeepScanResult(
    HardwareInventory? Hardware,
    WindowsInfo? Windows,
    IReadOnlyList<QueryResult<object>> ProbeResults,
    IReadOnlyList<DiagnosticFinding> Findings,
    DateTime StartedAt,
    DateTime CompletedAt,
    int FailedProbeCount)
{
    public bool HasFailures => FailedProbeCount > 0;
}

/// <summary>
/// Orchestrates the full hardware/Windows/software/storage inspection.
/// Each probe is isolated: failures are collected, never thrown.
/// </summary>
public sealed class DeepScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHardwareProbe _hardware;
    private readonly IWindowsProbe _windows;
    private readonly IDiagnosticEngine _diagnostics;
    private readonly IAppLogger _logger;
    private readonly string _rulesPath;

    public DeepScanner(
        IHardwareProbe hardware,
        IWindowsProbe windows,
        IDiagnosticEngine diagnostics,
        IAppLogger logger,
        string rulesPath)
    {
        _hardware = hardware;
        _windows = windows;
        _diagnostics = diagnostics;
        _logger = logger;
        _rulesPath = rulesPath;
    }

    public async Task<DeepScanResult> ScanAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        var results = new List<QueryResult<object>>();
        var failures = new List<string>();

        async Task ProbeAsync<T>(string label, Task<QueryResult<T>> task)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(label);
            var r = await task.ConfigureAwait(false);
            lock (results)
            {
                results.Add(QueryResult<object>.Ok(
                    r.ProbeName, r.Value!, r.Method, r.Duration));
                if (!r.Success)
                {
                    results[^1] = QueryResult<object>.Fail(
                        r.ProbeName, r.Error ?? "unknown", r.Method, r.Duration);
                    failures.Add($"{r.ProbeName}: {r.Error}");
                }
            }
        }

        // Hardware probes — run sequentially to keep WMI pressure low on old CPUs.
        await ProbeAsync("CPU", _hardware.GetCpuAsync(ct));
        await ProbeAsync("Memory", _hardware.GetMemoryAsync(ct));
        await ProbeAsync("Motherboard", _hardware.GetMotherboardAsync(ct));
        await ProbeAsync("BIOS", _hardware.GetBiosAsync(ct));
        await ProbeAsync("GPUs", _hardware.GetGpusAsync(ct));
        await ProbeAsync("Drives", _hardware.GetDrivesAsync(ct));
        await ProbeAsync("Volumes", _hardware.GetVolumesAsync(ct));
        await ProbeAsync("Devices", _hardware.GetDevicesAsync(ct));
        await ProbeAsync("TPM", _hardware.GetTpmStatusAsync(ct));
        await ProbeAsync("Secure Boot", _hardware.GetSecureBootAsync(ct));
        await ProbeAsync("Battery", _hardware.GetBatteryStatusAsync(ct));

        // Windows probes.
        await ProbeAsync("Windows info", _windows.GetWindowsInfoAsync(ct));
        await ProbeAsync("Updates", _windows.GetInstalledUpdatesAsync(ct));
        await ProbeAsync("Component store", _windows.GetComponentStoreHealthAsync(ct));
        await ProbeAsync("Defender", _windows.GetDefenderStatusAsync(ct));
        await ProbeAsync("Device errors", _windows.GetDeviceManagerErrorsAsync(ct));

        // Build the inventory from successful probes only.
        progress?.Report("Assembling inventory");
        var inventory = BuildInventory(results, failures);
        var windowsInfo = results
            .Where(r => r.ProbeName == "windows-info" && r.Success)
            .Select(r => (WindowsInfo)r.Value!)
            .FirstOrDefault();

        // Rule-based + AI correlation over collected evidence.
        progress?.Report("Analyzing evidence");
        var evidence = CollectEvidence(results, inventory, windowsInfo);
        var findings = await _diagnostics.AnalyzeAsync(evidence, ct).ConfigureAwait(false);

        // Evaluate bundled deterministic rules as well (never faked as AI).
        var ruleFindings = EvaluateRules(evidence, ct);
        var merged = ruleFindings.Concat(findings)
            .GroupBy(f => f.Id)
            .Select(g => g.First())
            .OrderByDescending(f => f.Severity)
            .ToList();

        return new DeepScanResult(
            inventory,
            windowsInfo,
            results,
            merged,
            started,
            DateTime.UtcNow,
            failures.Count);
    }

    private static HardwareInventory? BuildInventory(
        List<QueryResult<object>> results, List<string> failures)
    {
        T? Get<T>(string probe) where T : class
            => results.FirstOrDefault(r => r.ProbeName == probe && r.Success)?.Value as T;

        T? GetNullable<T>(string probe) where T : struct
            => results.FirstOrDefault(r => r.ProbeName == probe && r.Success)?.Value is T v ? v : null;

        var cpu = Get<CpuInfo>("cpu");
        var memory = Get<MemoryInfo>("memory");
        if (cpu is null || memory is null) return null;

        return new HardwareInventory(
            Cpu: cpu,
            Memory: memory,
            Motherboard: Get<MotherboardInfo>("motherboard")
                         ?? new MotherboardInfo("Unknown", "Unknown", "", "Unknown"),
            Bios: Get<BiosInfo>("bios") ?? new BiosInfo("Unknown", "Unknown", null, false),
            Gpus: Get<IReadOnlyList<GpuDeviceInfo>>("gpus") ?? Array.Empty<GpuDeviceInfo>(),
            Drives: Get<IReadOnlyList<DriveInfo>>("drives") ?? Array.Empty<DriveInfo>(),
            Volumes: Get<IReadOnlyList<VolumeInfo>>("volumes") ?? Array.Empty<VolumeInfo>(),
            Devices: Get<IReadOnlyList<DeviceEntry>>("devices") ?? Array.Empty<DeviceEntry>(),
            TpmStatus: Get<string>("tpm"),
            SecureBootEnabled: GetNullable<bool>("secureboot"),
            BatteryStatus: Get<string>("battery"),
            ProbeFailures: failures);
    }

    private static List<EvidenceItem> CollectEvidence(
        List<QueryResult<object>> results,
        HardwareInventory? inventory,
        WindowsInfo? windows)
    {
        var evidence = new List<EvidenceItem>();

        foreach (var r in results)
        {
            if (!r.Success)
            {
                evidence.Add(new EvidenceItem(
                    Source: $"probe:{r.ProbeName}",
                    Detail: $"Probe failed: {r.Error}",
                    EvidenceBasis.SystemEvidence));
                continue;
            }

            evidence.Add(new EvidenceItem(
                Source: $"probe:{r.ProbeName}",
                Detail: DescribeValue(r.Value),
                EvidenceBasis.SystemEvidence));
        }

        if (inventory is not null)
        {
            foreach (var dev in inventory.Devices.Where(d => d.Status != "OK"))
            {
                evidence.Add(new EvidenceItem(
                    Source: "device-manager",
                    Detail: $"Device problem: {dev.Name} ({dev.Status})",
                    EvidenceBasis.SystemEvidence));
            }

        foreach (var vol in inventory.Volumes.Where(v =>
                     v.SizeBytes > 0 && (double)v.FreeBytes / v.SizeBytes < 0.10))
        {
            evidence.Add(new EvidenceItem(
                Source: "storage",
                Detail: $"Low free space on {vol.Mount}: " +
                        $"{vol.FreeBytes / (1024 * 1024 * 1024):0.0} GB free of " +
                        $"{vol.SizeBytes / (1024 * 1024 * 1024):0.0} GB",
                EvidenceBasis.SystemEvidence));
        }
        }

        if (windows is not null)
        {
            evidence.Add(new EvidenceItem(
                Source: "windows",
                Detail: $"{windows.ProductName} {windows.Edition} build {windows.Build}; " +
                        $"activation: {windows.ActivationStatus}",
                EvidenceBasis.SystemEvidence));
        }

        return evidence;
    }

    private static string DescribeValue(object? value) => value switch
    {
        null => "no value",
        CpuInfo c => $"CPU {c.Name}: {c.PhysicalCores}C/{c.LogicalProcessors}T",
        MemoryInfo m => $"RAM {m.TotalBytes / (1024 * 1024 * 1024):0} GB total, " +
                        $"{m.AvailableBytes / (1024 * 1024 * 1024):0} GB available",
        MotherboardInfo mb => $"Board {mb.Manufacturer} {mb.Product}",
        BiosInfo b => $"BIOS {b.Manufacturer} {b.Version}",
        WindowsInfo w => $"{w.ProductName} build {w.Build}",
        IReadOnlyList<GpuDeviceInfo> g => $"GPUs: {string.Join(", ", g.Select(x => x.Name))}",
        IReadOnlyList<DriveInfo> d => $"Drives: {string.Join(", ", d.Select(x => x.Model))}",
        IReadOnlyList<VolumeInfo> v => $"Volumes: {string.Join(", ", v.Select(x => x.Mount))}",
        IReadOnlyList<DeviceEntry> dev => $"{dev.Count} devices, " +
                                          $"{dev.Count(d => d.Status != "OK")} with problems",
        IReadOnlyList<string> list => string.Join("; ", list.Take(5)),
        _ => value.ToString() ?? "value",
    };

    /// <summary>
    /// Deterministic rule evaluation over collected evidence. Rules come from the
    /// bundled diagnostic-rules JSON; results are explicitly rule-based.
    /// </summary>
    private List<DiagnosticFinding> EvaluateRules(
        IReadOnlyList<EvidenceItem> evidence, CancellationToken ct)
    {
        var findings = new List<DiagnosticFinding>();
        if (!File.Exists(_rulesPath))
        {
            _logger.Log(LogLevel.Warn, "DeepScanner",
                $"Rules file missing: {_rulesPath} — rule evaluation skipped.");
            return findings;
        }

        List<RuleDefinition> rules;
        try
        {
            rules = JsonSerializer.Deserialize<List<RuleDefinition>>(
                        File.ReadAllText(_rulesPath), JsonOptions)
                    ?? new List<RuleDefinition>();
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Error, "DeepScanner", "Rules file unreadable.", ex);
            return findings;
        }

        foreach (var rule in rules.Where(r => r.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!rule.Evaluate(evidence)) continue;
                findings.Add(new DiagnosticFinding
                {
                    Id = rule.Id,
                    Title = rule.Title,
                    Description = rule.Description,
                    Severity = Enum.TryParse<FindingSeverity>(
                        rule.Severity, true, out var sev)
                        ? sev : FindingSeverity.Low,
                    AffectedComponent = rule.Category,
                    Confidence = ConfidenceLevel.High,
                    Evidence = evidence
                        .Where(e => rule.EvidenceContains is null
                                   || rule.EvidenceContains.Any(c =>
                                       e.Detail.Contains(c, StringComparison.OrdinalIgnoreCase)))
                        .Take(8).ToList(),
                    PossibleCauses = rule.PossibleCauses,
                    RecommendedSolution = rule.Recommendation,
                    Risks = rule.Risks,
                    VerificationMethod = rule.Verification,
                    IsAiInterpreted = false,
                    UncertaintyExplanation =
                        "Rule-based inference from deterministic conditions (not LLM output).",
                });
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, "DeepScanner",
                    $"Rule {rule.Id} failed to evaluate.", ex);
            }
        }

        return findings;
    }

    private sealed class RuleDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Severity { get; set; } = "Low";
        public bool Enabled { get; set; } = true;
        public string[]? EvidenceContains { get; set; }
        public string[]? EvidenceNotContains { get; set; }
        public string[] PossibleCauses { get; set; } = Array.Empty<string>();
        public string Recommendation { get; set; } = string.Empty;
        public string Risks { get; set; } = string.Empty;
        public string Verification { get; set; } = string.Empty;

        public bool Evaluate(IReadOnlyList<EvidenceItem> evidence)
        {
            if (EvidenceContains is { Length: > 0 })
            {
                var any = evidence.Any(e =>
                    EvidenceContains.Any(c =>
                        e.Detail.Contains(c, StringComparison.OrdinalIgnoreCase)));
                if (!any) return false;
            }

            if (EvidenceNotContains is { Length: > 0 })
            {
                var blocked = evidence.Any(e =>
                    EvidenceNotContains.Any(c =>
                        e.Detail.Contains(c, StringComparison.OrdinalIgnoreCase)));
                if (blocked) return false;
            }

            return EvidenceContains is { Length: > 0 } || EvidenceNotContains is { Length: > 0 };
        }
    }
}
