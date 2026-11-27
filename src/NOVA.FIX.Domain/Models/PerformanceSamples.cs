namespace NOVA.FIX.Domain.Models;

/// <summary>Live CPU sample for dashboards and charts.</summary>
public sealed record CpuSample(
    DateTime Timestamp,
    double UsagePercent,
    double FrequencyMHz,
    double? TemperatureCelsius);

/// <summary>Live memory sample.</summary>
public sealed record MemorySample(
    DateTime Timestamp,
    long UsedBytes,
    long AvailableBytes,
    long CommittedBytes,
    long CachedBytes);

/// <summary>Live disk sample.</summary>
public sealed record DiskSample(
    DateTime Timestamp,
    double ActivePercent,
    double ReadBytesPerSec,
    double WriteBytesPerSec,
    double? ResponseTimeMs);

/// <summary>Live GPU sample; sensors may be unavailable on some hardware.</summary>
public sealed record GpuSample(
    DateTime Timestamp,
    string GpuName,
    double? UsagePercent,
    double? TemperatureCelsius,
    double? MemoryUsedBytes,
    double? MemoryTotalBytes);

/// <summary>Bounded monitoring history returned by the performance monitor.</summary>
public sealed record MonitoringSnapshot(
    IReadOnlyList<CpuSample> Cpu,
    IReadOnlyList<MemorySample> Memory,
    IReadOnlyList<DiskSample> Disk,
    IReadOnlyList<GpuSample> Gpu,
    bool IsPaused);

/// <summary>Honest sensor availability marker — never invent values.</summary>
public sealed record SensorValue<T>(T? Value, bool Available, string? Reason = null)
{
    public static SensorValue<T> Unavailable(string reason) => new(default, false, reason);
    public static SensorValue<T> From(T value) => new(value, true, null);
}
