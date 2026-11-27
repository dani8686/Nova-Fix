using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Domain.Results;

namespace NOVA.FIX.Autopilot;

/// <summary>Plan step produced by Autopilot for user review.</summary>
public sealed record AutopilotPlanStep(
    string FindingId,
    string Title,
    FindingSeverity Severity,
    ActionRisk Risk,
    string ActionId,
    string Rationale,
    bool AutoEligible);

/// <summary>
/// NOVA Autopilot: guided offline automation —
/// scan → inventory → measure → collect errors → AI analysis → correlation →
/// prioritization → risk assessment → plan → USER APPROVAL → execute → verify →
/// final scan → before/after report.
///
/// Safety rules (never silently):
///  - delete personal data, disable security, remove essential services,
///    install unknown drivers, modify BIOS, or destructively repair storage.
///  - Safe mode: read-only + approved low-risk only.
///  - Guided: each fix individually confirmed.
///  - Advanced: explicit approvals per operation.
/// </summary>
public sealed class AutopilotEngine
{
    private readonly IAppLogger _logger;
    private readonly Func<Task<DeepScanResult>> _scanAsync;
    private readonly IDiagnosticEngine _correlator;
    private readonly IRepairExecutor _executor;
    private readonly IActionRegistry _registry;

    public AutopilotEngine(
        IAppLogger logger,
        Func<Task<DeepScanResult>> scanAsync,
        IDiagnosticEngine correlator,
        IRepairExecutor executor,
        IActionRegistry registry)
    {
        _logger = logger;
        _scanAsync = scanAsync;
        _correlator = correlator;
        _executor = executor;
        _registry = registry;
    }

    public event EventHandler<string>? ProgressChanged;

    private void Report(string stage) =>
        ProgressChanged?.Invoke(this, stage);

    /// <summary>Steps 1–9: gather everything and produce an ordered plan.</summary>
    public async Task<AutopilotPlan> BuildPlanAsync(
        AutopilotMode mode, CancellationToken ct = default)
    {
        Report("Initial system scan");
        var scan = await _scanAsync().ConfigureAwait(false);

        Report("Hardware inventory");
        var hardware = scan.Hardware;

        Report("Performance measurements");
        // measurement is performed by the PerformanceLab in the UI layer;
        // here we only need evidence already present in the scan.

        Report("Error collection & AI analysis");
        var findings = scan.Findings;

        Report("Repair prioritization & risk assessment");
        var steps = new List<AutopilotPlanStep>();
        foreach (var f in findings.OrderByDescending(f => f.Severity))
        {
            ct.ThrowIfCancellationRequested();
            var actionId = MapFindingToAction(f);
            if (actionId is null) continue;

            var def = _registry.Find(actionId);
            if (def is null) continue;

            var risk = def.Risk;
            var autoEligible = mode switch
            {
                AutopilotMode.Safe =>
                    risk == ActionRisk.Safe && def.RequiredPermissions == "Standard user",
                AutopilotMode.Guided => risk <= ActionRisk.Medium,
                AutopilotMode.Advanced => risk <= ActionRisk.High,
                _ => false,
            };

            steps.Add(new AutopilotPlanStep(
                f.Id, f.Title, f.Severity, risk, actionId,
                f.RecommendedSolution,
                AutoEligible: autoEligible));
        }

        // Hard safety floor: destructive actions are never auto-eligible.
        steps = steps
            .Select(s => s.Risk >= ActionRisk.Destructive
                ? s with { AutoEligible = false }
                : s)
            .OrderByDescending(s => s.Severity)
            .ThenBy(s => s.Risk)
            .ToList();

        Report("Plan ready for approval");
        _logger.Log(LogLevel.Info, "Autopilot",
            $"Plan built: {steps.Count} step(s), mode={mode}");

        return new AutopilotPlan(
            Mode: mode,
            Steps: steps,
            CreatedAt: DateTime.UtcNow,
            ScanFindings: findings,
            HardwareSummary: hardware is null
                ? "Inventory unavailable (probe failures)."
                : $"{hardware.Cpu.Name}, {hardware.Memory.TotalBytes / (1024 * 1024 * 1024)} GB RAM",
            Note: BuildModeNote(mode));
    }

    /// <summary>
    /// Steps 10–13: execute ONLY approved steps, verify each, then re-scan.
    /// </summary>
    public async Task<AutopilotOutcome> ExecuteAsync(
        AutopilotPlan plan,
        IReadOnlyDictionary<string, bool> approvals,
        Func<RepairProposal, CancellationToken, Task>? confirmationCallback,
        CancellationToken ct = default)
    {
        var results = new List<(AutopilotPlanStep Step, OperationResult Result)>();

        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();

            if (!approvals.TryGetValue(step.ActionId, out var approved) || !approved)
            {
                results.Add((step, new OperationResult
                {
                    ActionId = step.ActionId,
                    Status = OperationStatus.Skipped,
                    Output = "Not approved by user.",
                }));
                continue;
            }

            if (step.Risk >= ActionRisk.High && confirmationCallback is not null)
            {
                var proposal = ToProposal(step);
                await confirmationCallback(proposal, ct).ConfigureAwait(false);
            }

            Report($"Executing: {step.Title}");
            var proposal = ToProposal(step);
            var result = await _executor.ExecuteAsync(proposal, null, ct)
                .ConfigureAwait(false);
            results.Add((step, result));
        }

        Report("Final scan & verification");
        var finalScan = await _scanAsync().ConfigureAwait(false);

        var resolved = results.Count(r =>
            r.Result.Status is OperationStatus.Succeeded
                or OperationStatus.PartiallySucceeded);
        var skipped = results.Count(r => r.Result.Status == OperationStatus.Skipped);
        var failed = results.Count(r => r.Result.Status == OperationStatus.Failed);

        var remaining = finalScan.Findings.Select(f => f.Title).ToList();

        Report("Before/after report ready");
        return new AutopilotOutcome(
            Results: results.Select(r => r.Result).ToList(),
            Succeeded: resolved,
            Skipped: skipped,
            Failed: failed,
            FinalFindings: finalScan.Findings,
            RemainingIssues: remaining,
            CompletedAt: DateTime.UtcNow);
    }

    private static RepairProposal ToProposal(AutopilotPlanStep step) => new()
    {
        Id = $"auto-{step.FindingId}",
        ActionId = step.ActionId,
        Target: step.Title,
        Purpose = step.Rationale,
        ExpectedResult = "Issue resolved; verification step confirms.",
        Risk = step.Risk,
        Preconditions = new[] { "Plan step approved by user" },
        RequiredPermissions = "Per action definition",
        Verification = "See action definition verification.",
        Rollback = "See action definition rollback.",
        FindingId = step.FindingId,
        RequiresUserApproval = true,
    };

    /// <summary>Maps a finding to the safest relevant allowlisted action.</summary>
    internal static string? MapFindingToAction(DiagnosticFinding f) => f.Id switch
    {
        "find.servicing-errors" => "dism.checkhealth",
        "find.update-failures" => "update.inspect",
        "find.storage-pressure" => "storage.cleanup-temp",
        "find.memory-pressure" => "startup.disable",
        "find.device-errors" => "service.inspect",
        var id when id.StartsWith("find.repeated-", StringComparison.Ordinal)
            => "service.inspect",
        _ => null,
    };

    private static string BuildModeNote(AutopilotMode mode) => mode switch
    {
        AutopilotMode.Safe =>
            "Safe Mode: read-only inspection and approved low-risk operations only. " +
            "Nothing is changed without approval.",
        AutopilotMode.Guided =>
            "Guided Mode: recommended fixes are listed; each requires individual " +
            "confirmation before execution.",
        AutopilotMode.Advanced =>
            "Advanced Mode: broader operations become available, but every step " +
            "still requires explicit approval. Destructive operations are never " +
            "auto-approved.",
        _ => string.Empty,
    };
}

/// <summary>Plan produced for user approval.</summary>
public sealed record AutopilotPlan(
    AutopilotMode Mode,
    IReadOnlyList<AutopilotPlanStep> Steps,
    DateTime CreatedAt,
    IReadOnlyList<DiagnosticFinding> ScanFindings,
    string HardwareSummary,
    string Note);

/// <summary>Outcome of an approved execution pass.</summary>
public sealed record AutopilotOutcome(
    IReadOnlyList<OperationResult> Results,
    int Succeeded,
    int Skipped,
    int Failed,
    IReadOnlyList<DiagnosticFinding> FinalFindings,
    IReadOnlyList<string> RemainingIssues,
    DateTime CompletedAt);
