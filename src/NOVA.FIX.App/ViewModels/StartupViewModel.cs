using System.Collections.ObjectModel;
using System.Windows;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Optimization;

namespace NOVA.FIX.App.ViewModels;

/// <summary>Startup Manager view-model: inspect, backup, disable/enable.</summary>
public sealed class StartupViewModel : ViewModelBase
{
    private readonly StartupManager _manager;
    private readonly AppPaths _paths;
    private StartupEntry? _selected;

    public StartupViewModel(ServiceContainer c)
    {
        _manager = c.Resolve<StartupManager>();
        _paths = c.Resolve<AppPaths>();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        BackupCommand = new AsyncRelayCommand(BackupAsync);
        DisableCommand = new AsyncRelayCommand(DisableAsync, () => Selected is not null);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand BackupCommand { get; }
    public AsyncRelayCommand DisableCommand { get; }

    public ObservableCollection<StartupEntry> Entries { get; } = new();

    public StartupEntry? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                DisableCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var entries = await Task.Run(() => _manager.Inspect(), ct);
            Entries.Clear();
            foreach (var e in entries) Entries.Add(e);
            StatusMessage = $"{Entries.Count} startup entr{(Entries.Count == 1 ? "y" : "ies")} found.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BackupAsync(CancellationToken ct)
    {
        var file = await Task.Run(
            () => _manager.ExportBackup(_paths.Backups), ct);
        StatusMessage = $"Original configuration exported: {file}";
    }

    private async Task DisableAsync(CancellationToken ct)
    {
        if (Selected is null) return;

        var confirm = MessageBox.Show(
            $"Disable '{Selected.Name}'?\n\nPath: {Selected.Path}\n" +
            "Only the startup registration is disabled — no application files " +
            "are deleted. You can re-enable it here anytime.",
            "NOVA FIX — Disable startup entry",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var (ok, message) = await Task.Run(
            () => _manager.Disable(Selected.Id), ct);
        StatusMessage = message;
        if (ok) await RefreshAsync(ct);
    }
}
