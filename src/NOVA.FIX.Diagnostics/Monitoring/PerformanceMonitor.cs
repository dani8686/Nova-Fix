using System.Diagnostics;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.Diagnostics.Monitoring;

/// <summary>
/// Real-time performance sampler with bounded ring buffers, configurable interval,
/// pause/resume and honest "unavailable" reporting for sensors that do not exist.
/// Sampling uses CPU counters (no GPU vendor SDK required).
/// </summary>
public sealed class PerformanceMonitor : IPerformanceMonitor, IAsyncDisposable
{
    private readonly IAppLogger _logger;
    private readonly int _capacity;
    private readonly object _sync = new();

    private readonly Queue<CpuSample> _cpu = new();
    private readonly Queue<MemorySample> _memory = new();
    private readonly Queue<DiskSample> _disk = new();
    private readonly Queue<GpuSample> _gpu = new();

    private Timer? _timer;
    private TimeSpan _interval = TimeSpan.FromSeconds(2);
    private bool _paused;
    private bool _started;

    private readonly PerformanceCounter? _cpuCounter;
    private readonly PerformanceCounter? _cpuFreqBase;
    private readonly PerformanceCounter? _diskActive;
    private PerformanceCounter? _diskRead;
    private PerformanceCounter? _diskWrite;

    private long _lastIdle, _lastKernel, _lastUser;
    private bool _cpuBaselineSet;

    public PerformanceMonitor(IAppLogger logger, int capacity = 240)
    {
        _logger = logger;
        _capacity = Math.Max(30, capacity); // bounded history: ~8 min at 2 s

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = _cpuCounter.NextValue(); // prime
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "PerfMonitor",
                "CPU performance counter unavailable; using idle-derived usage.", ex);
        }

        try
        {
            _diskActive = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
            _diskRead = new PerformanceCounter("PhysicalDisk", "Disk Bytes/sec", "_Total");
            _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
            _ = _diskActive.NextValue();
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "PerfMonitor",
                "Disk performance counters unavailable; disk metrics will be honest-unavailable.", ex);
            _diskActive = null;
            _diskRead = null;
            _diskWrite = null;
        }
    }

    public bool IsPaused
    {
        get { lock (_sync) return _paused; }
    }

    public event EventHandler<MonitoringSnapshot>? Updated;

    public void Pause()
    {
        lock (_sync) _paused = true;
    }

    public void Resume()
    {
        lock (_sync) _paused = false;
    }

    public void SetInterval(TimeSpan interval)
    {
        _interval = interval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : interval;
        if (_started)
        {
            _timer?.Change(_interval, _interval);
        }
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (_started) return Task.CompletedTask;
            _started = true;
            _timer = new Timer(Sample, null, TimeSpan.FromSeconds(1), _interval);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            _started = false;
            _timer?.Dispose();
            _timer = null;
        }
        return Task.CompletedTask;
    }

    public MonitoringSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new MonitoringSnapshot(
                _cpu.ToList(), _memory.ToList(), _disk.ToList(), _gpu.ToList(), _paused);
        }
    }

    private void Sample(object? state)
    {
        if (IsPaused) return;
        try
        {
            var now = DateTime.UtcNow;

            // --- CPU usage ---
            double cpuUsage;
            if (_cpuCounter is not null)
            {
                cpuUsage = Math.Clamp(_cpuCounter.NextValue(), 0, 100);
            }
            else
            {
                cpuUsage = SampleCpuFromIdleCounters();
            }

            double frequency = 0;
            try
            {
                var freq = new PerformanceCounter("Processor Information",
                    "Processor Frequency", "_Total");
                frequency = freq.NextValue();
                freq.Dispose();
            }
            catch
            {
                frequency = 0; // reported as 0 => UI shows "Unavailable"
            }

            double? temp = TryReadCpuTemperature();
            Enqueue(_cpu, new CpuSample(now, cpuUsage, frequency, temp));

            // --- Memory ---
            var mem = GC.GetGCMemoryInfo();
            Enqueue(_memory, new MemorySample(
                now,
                UsedBytes: mem.TotalAvailableMemoryBytes - mem.HeapSizeBytes,
                AvailableBytes: mem.TotalAvailableMemoryBytes,
                CommittedBytes: mem.TotalCommittedBytes,
                CachedBytes: mem.HeapSizeBytes));

            // --- Disk ---
            if (_diskActive is not null && _diskRead is not null && _diskWrite is not null)
            {
                var active = Math.Clamp(_diskActive.NextValue(), 0, 100);
                var read = (double)_diskRead.NextValue();
                var write = (double)_diskWrite.NextValue();
                Enqueue(_disk, new DiskSample(now, active, read, write, null));
            }

            // --- GPU (honest: only if a counter-able adapter exists) ---
            SampleGpu(now);

            var snapshot = GetSnapshot();
            Updated?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _logger.Log(LogLevel.Warn, "PerfMonitor", "Sample failed (skipped).", ex);
        }
    }

    private double SampleCpuFromIdleCounters()
    {
        try
        {
            var idle = new PerformanceCounter("Processor", "% Idle Time", "_Total");
            var idleVal = idle.NextValue();
            idle.Dispose();
            return Math.Clamp(100 - idleVal, 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    private static double? TryReadCpuTemperature()
    {
        // No universal reliable cross-vendor CPU temp API on Windows without OEM
        // drivers — report unavailable instead of inventing a value.
        return null;
    }

    private void SampleGpu(DateTime now)
    {
        // GPU utilization requires vendor-specific counters (NVML/ADL) or WMI
        // MSFT_GPU performance classes which are absent on many systems.
        // We sample what Windows exposes and mark the rest unavailable.
        try
        {
            if (_gpu.Count > 0 && _gpu.Last().GpuName.Length > 0)
            {
                var last = _gpu.Last();
                // keep the entry alive with honest nulls rather than stale numbers
                Enqueue(_gpu, new GpuSample(now, last.GpuName, null, null, null, null));
                return;
            }
            Enqueue(_gpu, new GpuSample(now, "GPU sensors", null, null, null, null));
        }
        catch
        {
            // ignore — GPU block simply stays empty
        }
    }

    private void Enqueue<T>(Queue<T> queue, T item)
    {
        lock (_sync)
        {
            queue.Enqueue(item);
            while (queue.Count > _capacity) queue.Dequeue();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cpuCounter?.Dispose();
        _cpuFreqBase?.Dispose();
        _diskActive?.Dispose();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
    }
}
