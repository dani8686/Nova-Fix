using NOVA.FIX.Core.Logging;
using NOVA.FIX.Diagnostics.Probes;
using NOVA.FIX.Domain.Contracts;
using Xunit;

namespace NOVA.FIX.IntegrationTests;

/// <summary>
/// Integration tests exercising REAL system probes on the Windows CI runner.
/// Every probe must either succeed or return an isolated failure — never throw.
/// </summary>
public class ProbeIntegrationTests
{
    private sealed class ConsoleLogger : IAppLogger
    {
        public void Log(LogLevel level, string category, string message,
            Exception? exception = null)
            => Console.WriteLine($"[{level}] {category}: {message}");

        public IReadOnlyList<string> RecentEntries(int count) => Array.Empty<string>();
    }

    [Fact]
    public async Task HardwareProbe_Cpu_ReturnsResultNotThrow()
    {
        var probe = new HardwareProbe(new ConsoleLogger());
        var result = await probe.GetCpuAsync();
        // Success depends on the runner; the contract is: no exception, honest result.
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.ProbeName));
        if (result.Success)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Value!.Name));
            Assert.True(result.Value.LogicalProcessors >= 1);
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(result.Error));
        }
    }

    [Fact]
    public async Task HardwareProbe_AllProbes_IndividuallyIsolated()
    {
        var probe = new HardwareProbe(new ConsoleLogger());
        var results = new[]
        {
            await probe.GetCpuAsync(),
            await probe.GetMemoryAsync(),
            await probe.GetMotherboardAsync(),
            await probe.GetBiosAsync(),
            await probe.GetGpusAsync(),
            await probe.GetDrivesAsync(),
            await probe.GetVolumesAsync(),
            await probe.GetDevicesAsync(),
            await probe.GetTpmStatusAsync(),
            await probe.GetSecureBootAsync(),
            await probe.GetBatteryStatusAsync(),
        };

        // None threw. At least CPU/memory/volumes should work on any Windows box.
        Assert.True(results.Count(r => r.Success) >= 3,
            "expected at least 3 successful probes on a Windows runner");
        Assert.All(results, r => Assert.False(
            r.Success && r.Value is null && r.ProbeName != "tpm",
            $"{r.ProbeName} success with null value"));
    }

    [Fact]
    public async Task WindowsProbe_ReturnsHonestInfo()
    {
        var probe = new WindowsProbe(new ConsoleLogger());
        var result = await probe.GetWindowsInfoAsync();
        Assert.NotNull(result);
        if (result.Success)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Value!.Build));
        }
    }

    [Fact]
    public async Task PerformanceMonitor_SamplesAndPauses()
    {
        await using var monitor = new Monitoring.PerformanceMonitor(
            new ConsoleLogger(), capacity: 30);
        await monitor.StartAsync();
        await Task.Delay(2500);
        var snap = monitor.GetSnapshot();
        Assert.True(snap.Cpu.Count >= 1, "expected at least one CPU sample");

        monitor.Pause();
        var pausedCount = monitor.GetSnapshot().Cpu.Count;
        await Task.Delay(2200);
        Assert.Equal(pausedCount, monitor.GetSnapshot().Cpu.Count);

        monitor.Resume();
        await Task.Delay(2200);
        Assert.True(monitor.GetSnapshot().Cpu.Count > pausedCount);

        await monitor.StopAsync();
    }
}
