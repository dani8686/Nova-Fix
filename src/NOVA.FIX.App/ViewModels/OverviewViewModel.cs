using System.Windows.Threading;
using NOVA.FIX.Core.Mvvm;
using NOVA.FIX.Core.Services;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.App.ViewModels;

/// <summary>System Overview: live charts + hardware/Windows summary cards.</summary>
public sealed class OverviewViewModel : ViewModelBase, IDisposable
{
    private readonly IPerformanceMonitor _monitor;
    private readonly IHardwareProbe _hardware;
    private readonly IWindowsProbe _windows;
    private readonly Dispatcher _dispatcher;

    private IReadOnlyList<CpuSample> _cpuSamples = Array.Empty<CpuSample>();
    private IReadOnlyList<MemorySample> _memorySamples = Array.Empty<MemorySample>();
    private IReadOnlyList<DiskSample> _diskSamples = Array.Empty<DiskSample>();
    private string _cpuText = "Sampling…";
    private string _ramText = "Sampling…";
    private string _diskText = "Sampling…";
    private string _gpuText = "Unavailable";
    private string _batteryText = "…";
    private string _windowsText = "…";
    private string _uptimeText = "…";
    private string _cpuHistory = string.Empty;
    private string _ramHistory = string.Empty;
    private bool _isPaused;

    public OverviewViewModel(ServiceContainer c)
    {
        _monitor = c.Resolve<IPerformanceMonitor>();
        _hardware = c.Resolve<IHardwareProbe>();
        _windows = c.Resolve<IWindowsProbe>();
        _dispatcher = Dispatcher.CurrentDispatcher;

        PauseResumeCommand = new AsyncRelayCommand(async ct =>
        {
            if (_monitor.IsPaused) _monitor.Resume();
            else _monitor.Pause();
            IsPaused = _monitor.IsPaused;
            await Task.CompletedTask;
        });

        _monitor.Updated += OnUpdated;
        _ = StartAsync();
    }

    public AsyncRelayCommand PauseResumeCommand { get; }

    public IReadOnlyList<CpuSample> CpuSamples
    {
        get => _cpuSamples;
        private set => SetProperty(ref _cpuSamples, value);
    }

    public IReadOnlyList<MemorySample> MemorySamples
    {
        get => _memorySamples;
        private set => SetProperty(ref _memorySamples, value);
    }

    public IReadOnlyList<DiskSample> DiskSamples
    {
        get => _diskSamples;
        private set => SetProperty(ref _diskSamples, value);
    }

    public string CpuText { get => _cpuText; private set => SetProperty(ref _cpuText, value); }
    public string RamText { get => _ramText; private set => SetProperty(ref _ramText, value); }
    public string DiskText { get => _diskText; private set => SetProperty(ref _diskText, value); }
    public string GpuText { get => _gpuText; private set => SetProperty(ref _gpuText, value); }
    public string BatteryText { get => _batteryText; private set => SetProperty(ref _batteryText, value); }
    public string WindowsText { get => _windowsText; private set => SetProperty(ref _windowsText, value); }
    public string UptimeText { get => _uptimeText; private set => SetProperty(ref _uptimeText, value); }
    public string CpuHistory { get => _cpuHistory; private set => SetProperty(ref _cpuHistory, value); }
    public string RamHistory { get => _ramHistory; private set => SetProperty(ref _ramHistory, value); }
    public bool IsPaused { get => _isPaused; private set => SetProperty(ref _isPaused, value); }

    private async Task StartAsync()
    {
        try
        {
            await _monitor.StartAsync();

            var cpu = await _hardware.GetCpuAsync();
            if (cpu.Success && cpu.Value is not null)
            {
                CpuText = $"{cpu.Value.Name}\n{cpu.Value.PhysicalCores}C / " +
                          $"{cpu.Value.LogicalProcessors}T";
            }
            else CpuText = "Unavailable";

            var win = await _windows.GetWindowsInfoAsync();
            if (win.Success && win.Value is not null)
            {
                WindowsText = $"{win.Value.ProductName} {win.Value.Edition}\n" +
                              $"build {win.Value.Build} ({win.Value.Architecture})";
            }
            else WindowsText = "Unavailable";

            var battery = await _hardware.GetBatteryStatusAsync();
            BatteryText = battery.Success ? battery.Value ?? "Unavailable" : "Unavailable";

            UptimeText = FormatUptime(DateTime.Now - System.Diagnostics.Process
                .GetCurrentProcess().StartTime);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Overview init: {ex.Message}";
        }
    }

    private void OnUpdated(object? sender, MonitoringSnapshot snap)
    {
        _dispatcher.BeginInvoke(() =>
        {
            CpuSamples = snap.Cpu;
            MemorySamples = snap.Memory;
            DiskSamples = snap.Disk;

            var lastCpu = snap.Cpu.LastOrDefault();
            if (lastCpu is not null)
            {
                CpuText = $"{lastCpu.UsagePercent:0.0}%  " +
                          (lastCpu.FrequencyMHz > 0
                              ? $"{lastCpu.FrequencyMHz / 1000:0.00} GHz"
                              : "freq Unavailable");
                CpuHistory = string.Join(",",
                    snap.Cpu.Skip(Math.Max(0, snap.Cpu.Count - 60))
                        .Select(s => s.UsagePercent.ToString("0.0")));
            }

            var lastMem = snap.Memory.LastOrDefault();
            if (lastMem is not null)
            {
                var total = lastMem.UsedBytes + lastMem.AvailableBytes;
                RamText = $"{lastMem.AvailableBytes / 1e9:0.0} GB free of " +
                          $"{total / 1e9:0.0} GB";
                RamHistory = string.Join(",",
                    snap.Memory.Skip(Math.Max(0, snap.Memory.Count - 60))
                        .Select(s =>
                        {
                            var t = s.UsedBytes + s.AvailableBytes;
                            return t == 0 ? "0" :
                                (s.UsedBytes * 100 / t).ToString("0.0");
                        }));
            }

            var lastDisk = snap.Disk.LastOrDefault();
            DiskText = lastDisk is null
                ? "Unavailable (counters not exposed)"
                : $"{lastDisk.ActivePercent:0.0}% active · " +
                  $"{(lastDisk.ReadBytesPerSec + lastDisk.WriteBytesPerSec) / 1024:0} KB/s";

            var lastGpu = snap.Gpu.LastOrDefault();
            GpuText = lastGpu?.UsagePercent is { } u
                ? $"{lastGpu.GpuName} {u:0.0}%"
                : "Unavailable — no vendor sensor exposed";
        });
    }

    private static string FormatUptime(TimeSpan t)
        => $"{(int)t.TotalDays}d {t.Hours:00}h {t.Minutes:00}m";

    public void Dispose()
    {
        _monitor.Updated -= OnUpdated;
        _ = _monitor.StopAsync();
    }
}
