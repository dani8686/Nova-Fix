using System.Collections.ObjectModel;
using System.Windows;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.App.ViewModels;

/// <summary>One-Click Repair: allowlisted workflows with explicit approvals.</summary>
public sealed class RepairViewModel : ViewModelBase
{
    private readonly IActionRegistry _registry;
    private readonly IRepairExecutor _executor;

    private RepairActionDefinition? _selected;
    private string _parametersJson = "{}";

    public RepairViewModel(ServiceContainer c)
    {
        _registry = c.Resolve<IActionRegistry>();
        _executor = c.Resolve<IRepairExecutor>();

        foreach (var a in _registry.All) Actions.Add(a);
        if (Actions.Count > 0) Selected = Actions[0];

        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync,
            () => Selected is not null && !IsBusy);
    }

    public AsyncRelayCommand ExecuteCommand { get; }

    public ObservableCollection<RepairActionDefinition> Actions { get; } = new();
    public ObservableCollection<string> Results { get; } = new();

    public RepairActionDefinition? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                ExecuteCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>JSON parameters for the action (e.g. {"serviceName":"wuauserv"}).</summary>
    public string ParametersJson
    {
        get => _parametersJson;
        set => SetProperty(ref _parametersJson, value);
    }

    private async Task ExecuteAsync(CancellationToken ct)
    {
        if (Selected is null) return;

        // Risk gate: anything above Safe requires explicit user confirmation.
        if (Selected.Risk > ActionRisk.Safe)
        {
            var confirm = MessageBox.Show(
                $"Run '{Selected.DisplayName}'?\n\n" +
                $"Target: {Selected.Target}\n" +
                $"Purpose: {Selected.Purpose}\n" +
                $"Expected: {Selected.ExpectedResult}\n" +
                $"Risk: {Selected.Risk}\n" +
                $"Permissions: {Selected.RequiredPermissions}\n" +
                $"Verification: {Selected.Verification}\n" +
                $"Rollback: {Selected.Rollback}\n\n" +
                "Preconditions:\n• " + string.Join("\n• ", Selected.Preconditions),
                "NOVA FIX — Confirm action",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                Results.Insert(0, $"{Selected.Id}: skipped (user declined).");
                return;
            }
        }

        IsBusy = true;
        try
        {
            var parameters = ParseParameters(ParametersJson);
            var proposal = new RepairProposal
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                ActionId = Selected.Id,
                Target = Selected.Target,
                Purpose = Selected.Purpose,
                ExpectedResult = Selected.ExpectedResult,
                Risk = Selected.Risk,
                Preconditions = Selected.Preconditions,
                RequiredPermissions = Selected.RequiredPermissions,
                Verification = Selected.Verification,
                Rollback = Selected.Rollback,
                RequiresUserApproval = true,
            };

            StatusMessage = $"Running {Selected.DisplayName}…";
            var result = await _executor.ExecuteAsync(proposal, parameters, ct);

            var line = $"{result.ActionId}: {result.Status}" +
                       (result.ExitCode is { } e ? $" (exit {e})" : string.Empty) +
                       (result.VerificationPassed is { } v ? $" · verified={v}" : string.Empty) +
                       (string.IsNullOrWhiteSpace(result.Error)
                           ? string.Empty
                           : $" · {result.Error}");
            Results.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
            StatusMessage = line;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Dictionary<string, string> ParseParameters(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer
                       .Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
