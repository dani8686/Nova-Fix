using NOVA.FIX.Domain.Models;
using DriveInfo = NOVA.FIX.Domain.Models.DriveInfo;
using NOVA.FIX.Domain.Results;

namespace NOVA.FIX.Domain.Contracts;

/// <summary>Hardware probing surface — every method returns an isolated result.</summary>
public interface IHardwareProbe
{
    Task<QueryResult<CpuInfo>> GetCpuAsync(CancellationToken ct = default);
    Task<QueryResult<MemoryInfo>> GetMemoryAsync(CancellationToken ct = default);
    Task<QueryResult<MotherboardInfo>> GetMotherboardAsync(CancellationToken ct = default);
    Task<QueryResult<BiosInfo>> GetBiosAsync(CancellationToken ct = default);
    Task<QueryResult<IReadOnlyList<GpuDeviceInfo>>> GetGpusAsync(CancellationToken ct = default);
    Task<QueryResult<IReadOnlyList<DriveInfo>>> GetDrivesAsync(CancellationToken ct = default);
    Task<QueryResult<IReadOnlyList<VolumeInfo>>> GetVolumesAsync(CancellationToken ct = default);
    Task<QueryResult<IReadOnlyList<DeviceEntry>>> GetDevicesAsync(CancellationToken ct = default);
    Task<QueryResult<string?>> GetTpmStatusAsync(CancellationToken ct = default);
    Task<QueryResult<bool?>> GetSecureBootAsync(CancellationToken ct = default);
    Task<QueryResult<string?>> GetBatteryStatusAsync(CancellationToken ct = default);
}

/// <summary>Windows OS probing surface.</summary>
public interface IWindowsProbe
{
    Task<QueryResult<WindowsInfo>> GetWindowsInfoAsync(CancellationToken ct = default);
    Task<QueryResult<IReadOnlyList<string>>> GetInstalledUpdatesAsync(CancellationToken ct = default);
    Task<QueryResult<string>> GetComponentStoreHealthAsync(CancellationToken ct = default);
    Task<QueryResult<string>> GetDefenderStatusAsync(CancellationToken ct = default);
    Task<QueryResult<IReadOnlyList<string>>> GetDeviceManagerErrorsAsync(CancellationToken ct = default);
}

/// <summary>Live performance sampling surface with bounded history.</summary>
public interface IPerformanceMonitor
{
    bool IsPaused { get; }
    void Pause();
    void Resume();
    void SetInterval(TimeSpan interval);
    MonitoringSnapshot GetSnapshot();
    event EventHandler<MonitoringSnapshot>? Updated;
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
}
