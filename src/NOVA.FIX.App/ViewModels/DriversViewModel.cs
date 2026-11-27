using System.Collections.ObjectModel;
using System.IO;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Drivers;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.App.ViewModels;

/// <summary>Driver Intelligence view-model: inventory, repository, comparison.</summary>
public sealed class DriversViewModel : ViewModelBase
{
    private readonly DriverIntelligence _drivers;

    public DriversViewModel(ServiceContainer c)
    {
        _drivers = c.Resolve<DriverIntelligence>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CompareCommand = new AsyncRelayCommand(CompareAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CompareCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }

    public ObservableCollection<DriverEntry> Installed { get; } = new();
    public ObservableCollection<DriverPackage> Repository { get; } = new();
    public ObservableCollection<string> Comparison { get; } = new();

    private async Task RefreshAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var installed = await Task.Run(() => _drivers.InstalledDrivers(), ct);
            var repo = await Task.Run(() => _drivers.LocalRepository(), ct);
            Installed.Clear();
            foreach (var d in installed) Installed.Add(d);
            Repository.Clear();
            foreach (var p in repo) Repository.Add(p);
            StatusMessage =
                $"{Installed.Count} installed drivers · {Repository.Count} local packages. " +
                "No online driver service is used or claimed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompareAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var notes = await Task.Run(() => _drivers.CompareVersions(), ct);
            Comparison.Clear();
            foreach (var n in notes) Comparison.Add(n);
            StatusMessage =
                "Comparison complete — 'newer available' is stated only when a " +
                "verified newer package exists locally.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportAsync(CancellationToken ct)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NOVA FIX", "backups");
        var file = await Task.Run(() => _drivers.ExportInventory(dir), ct);
        StatusMessage = $"Driver inventory exported: {file}";
    }
}
