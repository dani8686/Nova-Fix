using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Optimization;

namespace NOVA.FIX.App.ViewModels;

/// <summary>Service Manager view-model: inspect + guarded modifications.</summary>
public sealed class ServicesViewModel : ViewModelBase
{
    private readonly ServiceManager _manager;
    private ServiceEntry? _selected;
    private string _filter = string.Empty;

    public ServicesViewModel(ServiceContainer c)
    {
        _manager = c.Resolve<ServiceManager>();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        SnapshotCommand = new AsyncRelayCommand(SnapshotAsync, () => Selected is not null);
        SetManualCommand = new AsyncRelayCommand(SetManualAsync, () => Selected is not null);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SnapshotCommand { get; }
    public AsyncRelayCommand SetManualCommand { get; }

    public ObservableCollection<ServiceEntry> Services { get; } = new();
    public ObservableCollection<string> _all = new();

    public ServiceEntry? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                SnapshotCommand.RaiseCanExecuteChanged();
                SetManualCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsCriticalSelected));
            }
        }
    }

    public bool IsCriticalSelected =>
        Selected is not null && _manager.IsCritical(Selected.ServiceName);

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
                _ = ApplyFilterAsync();
        }
    }

    private IReadOnlyList<ServiceEntry> _cache = Array.Empty<ServiceEntry>();

    private async Task RefreshAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            _cache = await Task.Run(() => _manager.InspectAll(), ct);
            await ApplyFilterAsync();
            StatusMessage = $"{_cache.Count} services enumerated.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task ApplyFilterAsync()
    {
        var filter = Filter.Trim();
        Services.Clear();
        foreach (var s in _cache)
        {
            if (filter.Length > 0
                && !s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                && !s.ServiceName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            Services.Add(s);
        }
        return Task.CompletedTask;
    }

    private async Task SnapshotAsync(CancellationToken ct)
    {
        if (Selected is null) return;
        var file = await Task.Run(
            () => _manager.Snapshot(Selected.ServiceName, SnapshotDir()), ct);
        StatusMessage = $"Snapshot: {file}";
    }

    private async Task SetManualAsync(CancellationToken ct)
    {
        if (Selected is null) return;
        if (_manager.IsCritical(Selected.ServiceName))
        {
            StatusMessage =
                $"'{Selected.ServiceName}' is critical — NOVA FIX will not modify it.";
            return;
        }

        var confirm = MessageBox.Show(
            $"Set '{Selected.DisplayName}' start type to Manual?\n\n" +
            "Current: " + Selected.StartType + "\n" +
            "A snapshot will be captured first for rollback. The service will be " +
            "stopped and restarted in manual mode.",
            "NOVA FIX — Modify service",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await Task.Run(() =>
                _manager.Snapshot(Selected.ServiceName, SnapshotDir()), ct);
            var (ok, msg) = await Task.Run(() =>
                _manager.SetStartType(Selected.ServiceName,
                    System.ServiceProcess.ServiceStartMode.Manual,
                    snapshotExists: true), ct);
            StatusMessage = msg;
            if (ok) await RefreshAsync(ct);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string SnapshotDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NOVA FIX", "snapshots");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
