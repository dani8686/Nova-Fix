using System.Collections.ObjectModel;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Optimization;

namespace NOVA.FIX.App.ViewModels;

/// <summary>Performance Lab: real measurements + recommendations + before/after.</summary>
public sealed class PerformanceViewModel : ViewModelBase
{
    private readonly PerformanceLab _lab;
    private readonly StartupManager _startup;
    private LabMetrics? _before;
    private LabMetrics? _after;

    public PerformanceViewModel(ServiceContainer c)
    {
        _lab = c.Resolve<PerformanceLab>();
        _startup = c.Resolve<StartupManager>();

        MeasureCommand = new AsyncRelayCommand(MeasureAsync);
        RecommendCommand = new AsyncRelayCommand(RecommendAsync);
        CompareCommand = new AsyncRelayCommand(CompareAsync);
    }

    public AsyncRelayCommand MeasureCommand { get; }
    public AsyncRelayCommand RecommendCommand { get; }
    public AsyncRelayCommand CompareCommand { get; }

    public ObservableCollection<OptimizationRecommendation> Recommendations { get; } = new();
    public ObservableCollection<string> Comparison { get; } = new();
    public ObservableCollection<string> TopProcesses { get; } = new();

    public string MetricsText { get; private set; } = "No measurement yet.";

    private async Task MeasureAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var m = await Task.Run(() => _lab.Measure(), ct);
            _before = m;
            MetricsText =
                $"CPU {m.CpuPercent:0.0}% · used {m.UsedBytes / 1e9:0.0} GB · " +
                $"available {m.AvailableBytes / 1e9:0.0} GB";
            TopProcesses.Clear();
            foreach (var p in m.TopPrivateMemoryProcesses)
                TopProcesses.Add($"{p.Name} (PID {p.Id}): {p.Private / (1024 * 1024):0} MB");
            StatusMessage = "Baseline measured.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RecommendAsync(CancellationToken ct)
    {
        if (_before is null)
        {
            StatusMessage = "Measure first — recommendations require real data.";
            return;
        }

        var startups = await Task.Run(() => _startup.Inspect(), ct);
        Recommendations.Clear();
        foreach (var r in _lab.Recommend(_before, startups)) Recommendations.Add(r);
        StatusMessage = $"{Recommendations.Count} recommendation(s) from measurements.";
    }

    private async Task CompareAsync(CancellationToken ct)
    {
        if (_before is null)
        {
            StatusMessage = "Take the 'before' measurement first.";
            return;
        }

        IsBusy = true;
        try
        {
            _after = await Task.Run(() => _lab.Measure(), ct);
            Comparison.Clear();
            foreach (var m in PerformanceLab.Compare(_before, _after))
            {
                Comparison.Add(
                    $"{m.Name}: {m.Before:0.##} → {m.After:0.##} {m.Unit} " +
                    $"({m.PercentChange:+0.0;-0.0}%)");
            }
            StatusMessage = "Before/after comparison ready.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
