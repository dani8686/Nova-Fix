using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using NOVA.FIX.Autopilot;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Diagnostics.Scanner;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;

namespace NOVA.FIX.App.ViewModels;

/// <summary>NOVA Autopilot view-model: mode → plan → approval → execute → report.</summary>
public sealed class AutopilotViewModel : ViewModelBase
{
    private readonly AutopilotEngine _engine;
    private readonly IDiagnosticEngine _correlator;
    private readonly IRepairExecutor _executor;
    private readonly IActionRegistry _registry;
    private readonly DeepScanner _scanner;
    private readonly Domain.Contracts.IKnowledgeBase _knowledge;

    private AutopilotMode _mode = AutopilotMode.Safe;
    private AutopilotPlan? _plan;
    private string _phase = "Choose a mode and build the plan.";

    public AutopilotViewModel(ServiceContainer c)
    {
        _knowledge = c.Resolve<Domain.Contracts.IKnowledgeBase>();
        _scanner = c.Resolve<DeepScanner>();
        _correlator = c.Resolve<IDiagnosticEngine>();
        _executor = c.Resolve<IRepairExecutor>();
        _registry = c.Resolve<IActionRegistry>();

        _engine = new AutopilotEngine(
            c.Resolve<NOVA.FIX.Core.Logging.IAppLogger>(),
            () => _scanner.ScanAsync(null),
            _correlator, _executor, _registry);
        _engine.ProgressChanged += (_, msg) =>
            Dispatcher.CurrentDispatcher.BeginInvoke(() => Phase = msg);

        BuildPlanCommand = new AsyncRelayCommand(BuildPlanAsync, () => Plan is null);
        ApproveAllCommand = new AsyncRelayCommand(ApproveAllAsync, () => Plan is not null);
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync,
            () => Plan is not null && Approvals.Count > 0 && !IsBusy);
    }

    public AsyncRelayCommand BuildPlanCommand { get; }
    public AsyncRelayCommand ApproveAllCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }

    public IReadOnlyList<string> Modes { get; } =
        new[] { "Safe", "Guided", "Advanced" };

    public string SelectedMode
    {
        get => _mode.ToString();
        set
        {
            if (Enum.TryParse<AutopilotMode>(value, out var m) && _mode != m)
            {
                _mode = m;
                OnPropertyChanged();
                StatusMessage = $"Mode: {value} — {ModeHint(m)}";
            }
        }
    }

    public ObservableCollection<PlanStepRow> Steps { get; } = new();
    public ObservableCollection<string> OutcomeLines { get; } = new();

    /// <summary>actionId → user approval.</summary>
    public Dictionary<string, bool> Approvals { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AutopilotPlan? Plan
    {
        get => _plan;
        private set
        {
            if (SetProperty(ref _plan, value))
            {
                BuildPlanCommand.RaiseCanExecuteChanged();
                ApproveAllCommand.RaiseCanExecuteChanged();
                ExecuteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Phase
    {
        get => _phase;
        private set => SetProperty(ref _phase, value);
    }

    private static string ModeHint(AutopilotMode m) => m switch
    {
        AutopilotMode.Safe =>
            "read-only inspection and approved low-risk operations only",
        AutopilotMode.Guided =>
            "recommended fixes with individual confirmations",
        AutopilotMode.Advanced =>
            "advanced operations with explicit approvals (never silent destructive actions)",
        _ => string.Empty,
    };

    private async Task BuildPlanAsync(CancellationToken ct)
    {
        IsBusy = true;
        Steps.Clear();
        OutcomeLines.Clear();
        Approvals.Clear();
        try
        {
            StatusMessage = "Building plan (scan → analyze → prioritize → risk-assess)…";
            var plan = await _engine.BuildPlanAsync(_mode, ct);
            Plan = plan;

            foreach (var s in plan.Steps)
            {
                Approvals[s.ActionId] = false;
                Steps.Add(new PlanStepRow(s));
            }

            Phase = Steps.Count == 0
                ? "Plan ready: no actionable findings for this mode."
                : $"Plan ready: {Steps.Count} step(s). Review and approve — " +
                  "execution never starts without your approval.";
            StatusMessage = Phase;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Plan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ApproveAllAsync(CancellationToken ct)
    {
        // Only auto-eligible (low-risk) steps get pre-checked; the rest stay
        // unchecked for explicit individual approval.
        foreach (var row in Steps)
        {
            row.Approved = row.Step.AutoEligible;
            Approvals[row.Step.ActionId] = row.Approved;
            row.OnApprovalChanged();
        }
        StatusMessage = "Low-risk steps pre-approved — review high-risk steps individually.";
        ExecuteCommand.RaiseCanExecuteChanged();
        return Task.CompletedTask;
    }

    private async Task ExecuteAsync(CancellationToken ct)
    {
        if (Plan is null) return;

        // Sync approvals from rows.
        foreach (var row in Steps)
            Approvals[row.Step.ActionId] = row.Approved;

        if (!Approvals.Values.Any(a => a))
        {
            StatusMessage = "Approve at least one step before executing.";
            return;
        }

        var highRisk = Steps.Where(s =>
            s.Approved && s.Step.Risk >= ActionRisk.High).ToList();
        if (highRisk.Count > 0)
        {
            var confirm = MessageBox.Show(
                $"{highRisk.Count} high-risk step(s) approved:\n• " +
                string.Join("\n• ", highRisk.Select(h => h.Step.Title)) +
                "\n\nExecute them?",
                "NOVA FIX — Autopilot confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                StatusMessage = "Execution cancelled.";
                return;
            }
        }

        IsBusy = true;
        try
        {
            var outcome = await _engine.ExecuteAsync(Plan, Approvals, null, ct);
            OutcomeLines.Clear();
            OutcomeLines.Add($"Succeeded: {outcome.Succeeded} · " +
                             $"Skipped: {outcome.Skipped} · Failed: {outcome.Failed}");
            foreach (var r in outcome.Results)
                OutcomeLines.Add($"  {r.ActionId}: {r.Status}" +
                    (string.IsNullOrWhiteSpace(r.Error) ? "" : $" — {r.Error}"));
            OutcomeLines.Add(string.Empty);
            OutcomeLines.Add("Final scan — remaining issues:");
            foreach (var issue in outcome.RemainingIssues.Take(20))
                OutcomeLines.Add($"  • {issue}");

            Phase = "Execution complete — before/after data available in Report Center.";
            StatusMessage = $"Autopilot finished: {outcome.Succeeded} succeeded, " +
                            $"{outcome.Failed} failed, {outcome.Skipped} skipped.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled by user.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public sealed class PlanStepRow
    {
        public PlanStepRow(AutopilotPlanStep step) => Step = step;

        public AutopilotPlanStep Step { get; }

        public string Title => Step.Title;
        public string Severity => Step.Severity.ToString();
        public string Risk => Step.Risk.ToString();
        public string ActionId => Step.ActionId;
        public string Rationale => Step.Rationale;
        public bool Approved { get; set; }

        public void OnApprovalChanged() =>
            Changed?.Invoke(this, EventArgs.Empty);

        public event EventHandler? Changed;
    }
}
