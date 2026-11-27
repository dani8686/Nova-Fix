using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.AI.Correlation;

/// <summary>
/// Evidence-driven problem correlation engine (AI Problem Detective core).
///
/// Combines deterministic heuristics with optional local-LLM interpretation.
/// Safety rules encoded here:
///  - An ordinary Microsoft service consuming resources is NEVER called problematic.
///  - An old driver is NEVER assumed defective without device errors.
///  - Uncertainty is always explained.
///  - LLM output is marked probabilistic; rule output is marked rule-based.
/// </summary>
public sealed class EvidenceCorrelator : IDiagnosticEngine
{
    private readonly IKnowledgeBase _knowledge;
    private readonly ILocalLanguageModel? _llm;
    private readonly IAppLogger _logger;
    private readonly bool _allowAiInterpretation;

    public EvidenceCorrelator(
        IKnowledgeBase knowledge,
        ILocalLanguageModel? llm,
        IAppLogger logger,
        bool allowAiInterpretation = true)
    {
        _knowledge = knowledge;
        _llm = llm;
        _logger = logger;
        _allowAiInterpretation = allowAiInterpretation;
    }

    public async Task<IReadOnlyList<DiagnosticFinding>> AnalyzeAsync(
        IReadOnlyList<EvidenceItem> evidence,
        CancellationToken ct = default)
    {
        var findings = new List<DiagnosticFinding>();

        findings.AddRange(CorrelateDeviceProblems(evidence));
        findings.AddRange(CorrelateStoragePressure(evidence));
        findings.AddRange(CorrelateMemoryPressure(evidence));
        findings.AddRange(CorrelateServicingIssues(evidence));
        findings.AddRange(CorrelateUpdateIssues(evidence));
        findings.AddRange(await CorrelateRepeatedFailuresAsync(evidence, ct).ConfigureAwait(false));

        if (_allowAiInterpretation && _llm is { IsLoaded: true })
        {
            try
            {
                var aiFinding = await InterpretWithLanguageModelAsync(evidence, ct)
                    .ConfigureAwait(false);
                if (aiFinding is not null) findings.Add(aiFinding);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Warn, "Correlator",
                    "LLM interpretation failed; deterministic findings unaffected.", ex);
            }
        }

        // Never classify Microsoft services as problematic merely for resource use:
        // filter out any finding whose evidence is ONLY resource consumption by a
        // system service.
        return findings
            .Where(f => !IsResourceOnlyServiceFinding(f))
            .OrderByDescending(f => f.Severity)
            .ToList();
    }

    private static bool IsResourceOnlyServiceFinding(DiagnosticFinding f)
        => f.Title.Contains("service", StringComparison.OrdinalIgnoreCase)
           && f.Evidence.All(e => e.Detail.Contains("resource", StringComparison.OrdinalIgnoreCase)
                                  || e.Detail.Contains("CPU", StringComparison.OrdinalIgnoreCase)
                                  || e.Detail.Contains("memory", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<DiagnosticFinding> CorrelateDeviceProblems(
        IReadOnlyList<EvidenceItem> evidence)
    {
        var deviceProblems = evidence
            .Where(e => e.Source == "device-manager"
                        && !e.Detail.Contains("0 problems", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (deviceProblems.Count == 0) yield break;

        yield return new DiagnosticFinding
        {
            Id = "find.device-errors",
            Title = "Devices reporting problems in Device Manager",
            Description = $"{deviceProblems.Count} device(s) have non-zero problem codes. " +
                          "These commonly cause degraded functionality, blue screens, or " +
                          "peripheral failures.",
            Severity = deviceProblems.Count >= 3 ? FindingSeverity.High : FindingSeverity.Medium,
            AffectedComponent = "Device Manager",
            Confidence = deviceProblems.All(d => d.Basis == EvidenceBasis.SystemEvidence)
                ? ConfidenceLevel.Confirmed
                : ConfidenceLevel.Likely,
            Evidence = deviceProblems,
            PossibleCauses = new[] {
                "Corrupted or partially installed driver",
                "Device firmware incompatibility",
                "Hardware fault",
                "Resource conflict after hardware change" },
            RecommendedSolution =
                "Open Driver Intelligence → locate the failing device → verify the signed " +
                "local driver package → roll back or install the matching offline INF. " +
                "If a device is no longer used, disable it explicitly.",
            Risks = "Installing an incompatible driver can break the device or cause crashes. " +
                    "NOVA FIX never installs unsigned or mismatched drivers automatically.",
            VerificationMethod =
                "Re-scan Device Manager; the device status must return to 'OK' and the " +
                "problem code must clear.",
            KnowledgeCitation = "kb:driver-troubleshooting-basics",
        };
    }

    private static IEnumerable<DiagnosticFinding> CorrelateStoragePressure(
        IReadOnlyList<EvidenceItem> evidence)
    {
        var lowSpace = evidence
            .Where(e => e.Source == "storage"
                        && e.Detail.Contains("Low free space", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (lowSpace.Count == 0) yield break;

        yield return new DiagnosticFinding
        {
            Id = "find.storage-pressure",
            Title = "Low free disk space",
            Description = string.Join(" ", lowSpace.Select(e => e.Detail)) +
                          " Low free space degrades performance, prevents Windows updates " +
                          "and can corrupt user profiles.",
            Severity = FindingSeverity.High,
            AffectedComponent = "Storage",
            Confidence = ConfidenceLevel.Confirmed,
            Evidence = lowSpace,
            PossibleCauses = new[] {
                "Accumulated temporary files",
                "Large forgotten downloads or installers",
                "WinSxS/component store growth",
                "Hibernation file or system restore allocation" },
            RecommendedSolution =
                "Run Storage Guardian to review exact paths and sizes, then confirm a " +
                "targeted cleanup. Do not empty unknown directories blindly.",
            Risks = "Cleanup deletes files permanently. NOVA FIX always shows exact paths " +
                    "and sizes and requires confirmation — it never deletes user data automatically.",
            VerificationMethod = "Re-check free space after cleanup; target ≥ 15% free.",
            KnowledgeCitation = "kb:storage-cleanup-safety",
        };
    }

    private static IEnumerable<DiagnosticFinding> CorrelateMemoryPressure(
        IReadOnlyList<EvidenceItem> evidence)
    {
        var memEvidence = evidence.FirstOrDefault(e =>
            e.Source == "probe:memory"
            && e.Detail.Contains("available", StringComparison.OrdinalIgnoreCase));

        if (memEvidence is null) yield break;

        // Parse "RAM 16 GB total, 1 GB available"
        var match = System.Text.RegularExpressions.Regex.Match(
            memEvidence.Detail, @"(\d+) GB available");
        if (!match.Success) yield break;
        var availableGb = int.Parse(match.Groups[1].Value);
        var totalGbMatch = System.Text.RegularExpressions.Regex.Match(
            memEvidence.Detail, @"(\d+) GB total");
        var totalGb = totalGbMatch.Success ? int.Parse(totalGbMatch.Groups[1].Value) : 16;

        if (availableGb > Math.Max(1, totalGb / 5)) yield break;

        yield return new DiagnosticFinding
        {
            Id = "find.memory-pressure",
            Title = "System is under heavy memory pressure",
            Description = $"Only {availableGb} GB of {totalGb} GB RAM available at scan time. " +
                          "Heavy paging causes disk stutter and application hangs.",
            Severity = availableGb <= 1 ? FindingSeverity.Critical : FindingSeverity.Medium,
            AffectedComponent = "Memory",
            Confidence = ConfidenceLevel.Likely,
            Evidence = new[] { memEvidence },
            PossibleCauses = new[] {
                "Memory-hungry background applications",
                "Startup programs holding working sets",
                "A memory leak in a long-running process",
                "Insufficient RAM for the current workload" },
            RecommendedSolution =
                "Use Performance Lab to identify top private-memory processes, then " +
                "disable unnecessary startup entries in Startup Manager.",
            Risks = "Disabling a startup entry does not uninstall anything and is reversible " +
                    "from the same manager.",
            VerificationMethod =
                "Restart, then re-check available RAM after boot settles (2 minutes).",
            KnowledgeCitation = "kb:memory-pressure-basics",
        };
    }

    private static IEnumerable<DiagnosticFinding> CorrelateServicingIssues(
        IReadOnlyList<EvidenceItem> evidence)
    {
        var servicing = evidence
            .Where(e => e.Detail.Contains("0x800f", StringComparison.OrdinalIgnoreCase)
                        || e.Detail.Contains("component store", StringComparison.OrdinalIgnoreCase)
                           && e.Detail.Contains("fail", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (servicing.Count == 0) yield break;

        yield return new DiagnosticFinding
        {
            Id = "find.servicing-errors",
            Title = "Windows servicing errors detected",
            Description = "Event evidence indicates component-store/servicing failures. " +
                          "These typically surface as update error 0x800fxxxx codes.",
            Severity = FindingSeverity.High,
            AffectedComponent = "Windows Servicing",
            Confidence = ConfidenceLevel.Likely,
            Evidence = servicing,
            PossibleCauses = new[] {
                "Interrupted update installation",
                "Corrupted component store metadata",
                "Third-party antivirus interfering with servicing" },
            RecommendedSolution =
                "Run One-Click Repair → Windows Component Repair: DISM CheckHealth → " +
                "ScanHealth → RestoreHealth, then SFC /scannow. Offline MSU packages can " +
                "be installed from the Offline Update Manager if RestoreHealth needs a source.",
            Risks = "DISM RestoreHealth modifies the component store; a restore point is " +
                    "created first when the setting is enabled. Duration: 10–30 minutes.",
            VerificationMethod =
                "DISM /Online /Cleanup-Image /CheckHealth must report no corruption; " +
                "subsequent update attempt must progress.",
            KnowledgeCitation = "kb:windows-update-error-0x800f0805",
        };
    }

    private static IEnumerable<DiagnosticFinding> CorrelateUpdateIssues(
        IReadOnlyList<EvidenceItem> evidence)
    {
        var updateErrors = evidence
            .Where(e => e.Detail.Contains("Windows Update", StringComparison.OrdinalIgnoreCase)
                        && (e.Detail.Contains("error", StringComparison.OrdinalIgnoreCase)
                            || e.Detail.Contains("failed", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (updateErrors.Count == 0) yield break;

        yield return new DiagnosticFinding
        {
            Id = "find.update-failures",
            Title = "Windows Update failures in event history",
            Description = "Repeated update failures were observed in local event logs.",
            Severity = FindingSeverity.Medium,
            AffectedComponent = "Windows Update",
            Confidence = ConfidenceLevel.Likely,
            Evidence = updateErrors,
            PossibleCauses = new[] {
                "Corrupted update cache (SoftwareDistribution)",
                "Stuck update service state",
                "Insufficient disk space",
                "Pending servicing requiring reboot" },
            RecommendedSolution =
                "Run One-Click Repair → Windows Update: diagnose service state, reset the " +
                "update cache, then retry. Use Offline Update Manager for MSU/CAB packages.",
            Risks = "Resetting the update cache deletes downloaded update metadata only — " +
                    "not user data. Installed updates are unaffected.",
            VerificationMethod = "Next update check completes without 0x800f/0x8007 errors.",
            KnowledgeCitation = "kb:windows-update-cache-reset",
        };
    }

    private async Task<IEnumerable<DiagnosticFinding>> CorrelateRepeatedFailuresAsync(
        IReadOnlyList<EvidenceItem> evidence, CancellationToken ct)
    {
        // Repeated background failures: same source reported 3+ times.
        var repeated = evidence
            .GroupBy(e => e.Source)
            .Where(g => g.Count() >= 3
                        && g.All(x => x.Detail.Contains("fail", StringComparison.OrdinalIgnoreCase)
                                      || x.Detail.Contains("error", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (repeated.Count == 0)
            return Array.Empty<DiagnosticFinding>();

        await Task.Yield();
        return repeated.Select(g => new DiagnosticFinding
        {
            Id = $"find.repeated-{Sanitize(g.Key)}",
            Title = $"Repeated failures from {g.Key}",
            Description = $"The source '{g.Key}' reported {g.Count()} failure entries in the " +
                          "collected evidence. Repeated background failures waste resources " +
                          "and usually indicate a persistent underlying fault.",
            Severity = g.Count() >= 10 ? FindingSeverity.High : FindingSeverity.Medium,
            AffectedComponent = g.Key,
            Confidence = ConfidenceLevel.Likely,
            Evidence = g.Take(6).ToList(),
            PossibleCauses = new[] {
                "Service crash-loop",
                "Failing driver reset",
                "Permission or dependency problem" },
            RecommendedSolution =
                "Inspect the component in Service/Driver Manager, review its failure history, " +
                "and apply the guided repair workflow for that component.",
            Risks = "Investigation is read-only until you approve a repair.",
            VerificationMethod = "No further failure entries from the source after the fix " +
                                 "(re-scan).",
            UncertaintyExplanation =
                "Correlation is based on occurrence frequency in local evidence; the root " +
                "cause may differ and must be confirmed with the verification step.",
        }).ToList();
    }

    private async Task<DiagnosticFinding?> InterpretWithLanguageModelAsync(
        IReadOnlyList<EvidenceItem> evidence, CancellationToken ct)
    {
        if (_llm is null) return null;

        var topEvidence = string.Join("\n",
            evidence.Take(20).Select(e => $"[{e.Source}] {e.Detail}"));

        var prompt =
            "You are a Windows diagnostics analyst. Based ONLY on the evidence below, " +
            "return a JSON object with keys: title, summary, causes (array of 2-4), " +
            "recommendation, uncertainty. Be concise and never invent facts not in the evidence.\n" +
            $"EVIDENCE:\n{topEvidence}\nJSON:";

        var raw = await _llm.GenerateAsync(prompt, maxTokens: 400, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var parsed = JsonSummary.TryParse(raw, out var summary);
        if (!parsed || summary is null) return null;

        return new DiagnosticFinding
        {
            Id = $"find.ai-{Guid.NewGuid():N}"[..24],
            Title = summary.Title ?? "AI-assisted evidence summary",
            Description = summary.Summary ?? string.Empty,
            Severity = FindingSeverity.Low,
            AffectedComponent = "Cross-cutting analysis",
            Confidence = ConfidenceLevel.Plausible,
            Evidence = evidence.Take(6).ToList(),
            PossibleCauses = summary.Causes ?? new[] { "See evidence." },
            RecommendedSolution = summary.Recommendation ?? "Review the evidence above.",
            Risks = "Probabilistic AI interpretation — verify against evidence before acting.",
            VerificationMethod = "Confirm the described condition exists, then re-scan.",
            IsAiInterpreted = true,
            UncertaintyExplanation =
                "Generated by the bundled local language model (probabilistic interpretation, " +
                "not deterministic proof). Model profile: " +
                (_llm.ModelProfile ?? "unknown") + ".",
            KnowledgeCitation = null,
        };
    }

    private static string Sanitize(string input)
    {
        var chars = input.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).ToLowerInvariant();
    }

    private sealed class JsonSummary
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string[]? Causes { get; set; }
        public string? Recommendation { get; set; }

        public static bool TryParse(string raw, out JsonSummary? summary)
        {
            summary = null;
            try
            {
                // The model may wrap JSON in prose — extract the outermost braces.
                var start = raw.IndexOf('{');
                var end = raw.LastIndexOf('}');
                if (start < 0 || end <= start) return false;
                var json = raw[start..(end + 1)];
                summary = System.Text.Json.JsonSerializer.Deserialize<JsonSummary>(json,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });
                return summary is not null;
            }
            catch
            {
                return false;
            }
        }
    }
}
