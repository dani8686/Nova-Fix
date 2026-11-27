using System.Collections.ObjectModel;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Diagnostics.Scanner;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.App.ViewModels;

/// <summary>Deep Scanner view-model: runs the full isolated-probe scan.</summary>
public sealed class ScannerViewModel : ViewModelBase
{
    private readonly DeepScanner _scanner;
    private CancellationTokenSource? _cts;

    private bool _isScanning;
    private string _progress = string.Empty;
    private DeepScanResult? _lastResult;

    public ScannerViewModel(ServiceContainer c)
    {
        _scanner = c.Resolve<DeepScanner>();
        ScanCommand = new AsyncRelayCommand(RunScanAsync, () => !IsScanning);
        CancelCommand = new RelayCommand(() => _cts?.Cancel());
    }

    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }

    public ObservableCollection<DiagnosticFinding> Findings { get; } = new();
    public ObservableCollection<string> ProbeFailures { get; } = new();
    public ObservableCollection<string> ProbeLog { get; } = new();

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(IsNotScanning));
                ScanCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsNotScanning => !IsScanning;

    public string Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string Summary { get; private set; } = "Run a scan to inspect hardware, Windows and storage.";

    public DeepScanResult? LastResult => _lastResult;

    private async Task RunScanAsync(CancellationToken ct)
    {
        IsScanning = true;
        Findings.Clear();
        ProbeFailures.Clear();
        ProbeLog.Clear();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            var progress = new Progress<string>(s =>
            {
                Progress = s;
                ProbeLog.Add($"{DateTime.Now:HH:mm:ss}  {s}");
            });

            var result = await _scanner.ScanAsync(progress, _cts.Token);
            _lastResult = result;

            foreach (var f in result.Findings) Findings.Add(f);
            foreach (var fail in result.ProbeResults.Where(r => !r.Success))
                ProbeFailures.Add($"{fail.ProbeName}: {fail.Error}");

            Summary = $"Scan completed in " +
                      $"{(result.CompletedAt - result.StartedAt).TotalSeconds:0.0}s — " +
                      $"{result.ProbeResults.Count} probes, " +
                      $"{result.FailedProbeCount} failed, " +
                      $"{result.Findings.Count} finding(s).";
            Progress = "Done";
        }
        catch (OperationCanceledException)
        {
            Summary = "Scan cancelled by user.";
        }
        catch (Exception ex)
        {
            Summary = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }
}
