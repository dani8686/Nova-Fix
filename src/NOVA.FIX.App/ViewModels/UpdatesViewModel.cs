using System.Collections.ObjectModel;
using Microsoft.Win32;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Updates;

namespace NOVA.FIX.App.ViewModels;

/// <summary>Offline Update Manager view-model: import → inspect → approve → install.</summary>
public sealed class UpdatesViewModel : ViewModelBase
{
    private readonly OfflineUpdateManager _manager;
    private UpdatePackageInfo? _selected;

    public UpdatesViewModel(ServiceContainer c)
    {
        _manager = c.Resolve<OfflineUpdateManager>();
        BrowseCommand = new RelayCommand(Browse);
        InstallCommand = new AsyncRelayCommand(InstallAsync,
            () => Selected is not null && !IsBusy);
        HistoryCommand = new AsyncRelayCommand(HistoryAsync);
    }

    public RelayCommand BrowseCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand HistoryCommand { get; }

    public ObservableCollection<UpdatePackageInfo> Imported { get; } = new();
    public ObservableCollection<UpdateInstallRecord> History { get; } = new();

    public UpdatePackageInfo? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                InstallCommand.RaiseCanExecuteChanged();
        }
    }

    private void Browse()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select a local update package",
            Filter = "Update packages (*.msu;*.cab;*.zip;*.exe;*.inf;*.json)|" +
                     "*.msu;*.cab;*.zip;*.exe;*.inf;*.json|All files|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        _ = ImportAsync(dlg.FileNames);
    }

    private async Task ImportAsync(IEnumerable<string> files)
    {
        IsBusy = true;
        try
        {
            foreach (var f in files)
            {
                var info = await _manager.ImportAsync(f);
                if (info is not null) Imported.Add(info);
            }
            StatusMessage = Imported.Count > 0
                ? $"{Imported.Count} package(s) inspected: signature, hash, " +
                  "compatibility and version checked — none executed blindly."
                : "No packages imported.";
            if (Imported.Count > 0) Selected = Imported[^1];
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallAsync(CancellationToken ct)
    {
        if (Selected is null) return;
        var p = Selected;

        var detail =
            $"Package: {p.FileName}\n" +
            $"Kind: {p.PackageKind}\n" +
            $"Version: {p.Version ?? "unknown"}\n" +
            $"Size: {p.SizeBytes / 1e6:0.0} MB\n" +
            $"SHA-256: {p.Sha256[..16]}…\n" +
            $"Signature: {p.SignatureStatus}\n" +
            $"Compatibility: {p.CompatibilityStatus}\n" +
            $"Notes: {p.Notes ?? "—"}\n\n" +
            "Install this package now?";

        var confirm = System.Windows.MessageBox.Show(
            detail, "NOVA FIX — Approve update",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes)
        {
            var rejected = await _manager.InstallAsync(p, userApproved: false, ct);
            StatusMessage = "Installation rejected — recorded in history.";
            return;
        }

        IsBusy = true;
        try
        {
            var record = await _manager.InstallAsync(p, userApproved: true, ct);
            StatusMessage = $"{record.Result}: {record.Detail}";
            await HistoryAsync(ct);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task HistoryAsync(CancellationToken ct)
    {
        var rows = await _manager.HistoryAsync(100, ct);
        History.Clear();
        foreach (var r in rows) History.Add(r);
    }
}
