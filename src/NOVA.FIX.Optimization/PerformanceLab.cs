using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.Optimization;

/// <summary>One measured metric before/after an optimization.</summary>
public sealed record BeforeAfterMetric(string Name, double Before, double After, string Unit)
{
    public double Delta => After - Before;
    public double PercentChange => Before == 0 ? 0 : (After - Before) / Before * 100;
}

/// <summary>A concrete, evidence-backed optimization recommendation.</summary>
public sealed record OptimizationRecommendation(
    string Id,
    string Title,
    string Detail,
    string ExpectedImpact,
    string Risk,
    bool RequiresConfirmation,
    string RelatedActionId);

/// <summary>
/// Performance Lab: measures real counters and produces recommendations tied to
/// measurable metrics. No arbitrary "optimization scores" — only observed values.
/// </summary>
public sealed class PerformanceLab
{
    private readonly IAppLogger _logger;

    public PerformanceLab(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <summary>Snapshot of the metrics used for before/after comparison.</summary>
    public LabMetrics Measure()
    {
        long used = 0, available = 0;
        try
        {
            var mem = GC.GetGCMemoryInfo();
            available = mem.TotalAvailableMemoryBytes;
            used = mem.TotalAvailableMemoryBytes - mem.HeapSizeBytes;
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "PerfLab", "Memory measure failed.", ex);
        }

        double cpu = 0;
        try
        {
            using var counter = new System.Diagnostics.PerformanceCounter(
                "Processor", "% Processor Time", "_Total");
            _ = counter.NextValue();
            cpu = Math.Clamp(counter.NextValue(), 0, 100);
        }
        catch
        {
            // counter unavailable — reported as 0 with honest note in UI
        }

        var topProcesses = System.Diagnostics.Process.GetProcesses()
            .Select(p =>
            {
                try
                {
                    return (Name: p.ProcessName, Private: p.PrivateMemorySize64, Id: p.Id);
                }
                catch
                {
                    return (Name: p.ProcessName, Private: 0L, Id: p.Id);
                }
            })
            .OrderByDescending(x => x.Private)
            .Take(8)
            .ToList();

        return new LabMetrics(
            CpuPercent: cpu,
            UsedBytes: used,
            AvailableBytes: available,
            TopPrivateMemoryProcesses: topProcesses);
    }

    /// <summary>
    /// Recommendations derived from real measurements + collected evidence.
    /// Each one is actionable through the allowlisted registry.
    /// </summary>
    public IReadOnlyList<OptimizationRecommendation> Recommend(
        LabMetrics metrics, IReadOnlyList<StartupEntry> startupEntries)
    {
        var recs = new List<OptimizationRecommendation>();

        var disableable = startupEntries
            .Where(s => s.Status == "Enabled"
                        && s.Classification is "User Application" or "Update/Agent")
            .ToList();

        if (disableable.Count > 0)
        {
            recs.Add(new OptimizationRecommendation(
                "opt.startup",
                $"Review {disableable.Count} non-essential startup entries",
                "Entries: " + string.Join(", ", disableable.Take(5).Select(d => d.Name)) +
                ". Disabling them shortens boot time; no application files are deleted.",
                "Faster logon, lower idle memory",
                "Low — reversible in Startup Manager",
                RequiresConfirmation: true,
                RelatedActionId: "startup.disable"));
        }

        if (metrics.AvailableBytes > 0 && metrics.UsedBytes > 0)
        {
            var totalRatio = (double)metrics.UsedBytes /
                             (metrics.UsedBytes + metrics.AvailableBytes);
            if (totalRatio > 0.85)
            {
                recs.Add(new OptimizationRecommendation(
                    "opt.memory",
                    "Memory pressure detected",
                    $"~{totalRatio:P0} of RAM is in use. Inspect the top private-memory " +
                    "processes listed above and close or disable unnecessary background " +
                    "applications.",
                    "Smoother multitasking, less paging",
                    "Low",
                    RequiresConfirmation: true,
                    RelatedActionId: "startup.disable"));
            }
        }

        if (metrics.TopPrivateMemoryProcesses.Count > 0)
        {
            var top = metrics.TopPrivateMemoryProcesses[0];
            recs.Add(new OptimizationRecommendation(
                "opt.top-process",
                $"Largest memory consumer: {top.Name}",
                $"{top.Name} holds {top.Private / (1024 * 1024):0} MB private memory " +
                "(PID {top.Id}). Verify whether it is needed at startup.",
                "Informed decision — measurement, not guesswork",
                "Informational",
                RequiresConfirmation: false,
                RelatedActionId: "service.inspect"));
        }

        if (metrics.CpuPercent >= 90)
        {
            recs.Add(new OptimizationRecommendation(
                "opt.cpu-sustained",
                "Sustained high CPU usage",
                $"Total CPU is at {metrics.CpuPercent:0}%. Identify the process in " +
                "Task Manager; a crash-looping service or failing driver often causes " +
                "this pattern (see AI Problem Detective).",
                "Root-cause identification",
                "Informational",
                RequiresConfirmation: false,
                RelatedActionId: "service.inspect"));
        }

        if (recs.Count == 0)
        {
            recs.Add(new OptimizationRecommendation(
                "opt.none",
                "No optimization needed",
                "Current measurements are within normal ranges. NOVA FIX does not " +
                "invent optimizations that measurements do not justify.",
                "—",
                "None",
                RequiresConfirmation: false,
                RelatedActionId: string.Empty));
        }

        return recs;
    }

    public static IReadOnlyList<BeforeAfterMetric> Compare(
        LabMetrics before, LabMetrics after)
        => new List<BeforeAfterMetric>
        {
            new("CPU usage", before.CpuPercent, after.CpuPercent, "%"),
            new("Used memory", before.UsedBytes / 1e9, after.UsedBytes / 1e9, "GB"),
            new("Available memory", before.AvailableBytes / 1e9,
                after.AvailableBytes / 1e9, "GB"),
        };
}

/// <summary>Point-in-time performance metrics.</summary>
public sealed record LabMetrics(
    double CpuPercent,
    long UsedBytes,
    long AvailableBytes,
    IReadOnlyList<(string Name, long Private, int Id)> TopPrivateMemoryProcesses);
