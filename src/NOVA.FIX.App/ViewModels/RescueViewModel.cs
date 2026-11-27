using System.Collections.ObjectModel;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Recovery;

namespace NOVA.FIX.App.ViewModels;

/// <summary>Rescue &amp; Restore view-model: restore points, snapshots, journal.</summary>
public sealed class RescueViewModel : ViewModelBase
{
    private readonly RescueManager _rescue;

    public RescueViewModel(ServiceContainer c)
    {
        _rescue = c.Resolve<RescueManager>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CreateRestorePointCommand = new AsyncRelayCommand(CreateRestorePointAsync);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateRestorePointCommand { get; }

    public ObservableCollection<RestorePointEntry> RestorePoints { get; } = new();
    public ObservableCollection<ConfigurationSnapshot> Snapshots { get; } = new();
    public ObservableCollection<ChangeRecord> Journal { get; } = new();

    public string Disclaimer => RescueManager.RestorePointDisclaimer;

    private async Task RefreshAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var points = await Task.Run(() => _rescue.ListRestorePoints(), ct);
            var snaps = await Task.Run(() => _rescue.ListSnapshots(), ct);
            var journal = await Task.Run(() => _rescue.HistoryAsync(200, ct), ct);

            RestorePoints.Clear();
            foreach (var p in points) RestorePoints.Add(p);
            Snapshots.Clear();
            foreach (var s in snaps) Snapshots.Add(s);
            Journal.Clear();
            foreach (var j in journal) Journal.Add(j);

            StatusMessage =
                $"{RestorePoints.Count} restore points · {Snapshots.Count} snapshots · " +
                $"{Journal.Count} journal entries.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateRestorePointAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var (ok, msg) = await Task.Run(() =>
                _rescue.CreateRestorePoint(
                    "NOVA FIX pre-repair " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")),
                ct);
            StatusMessage = msg;
            if (ok) await RefreshAsync(ct);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
