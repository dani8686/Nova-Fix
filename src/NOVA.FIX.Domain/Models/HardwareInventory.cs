namespace NOVA.FIX.Domain.Models;

/// <summary>Hardware inventory record for the Deep Scanner (CPU example).</summary>
public sealed record CpuInfo(
    string Name,
    string Manufacturer,
    string? Socket,
    double? MaxClockMHz,
    int PhysicalCores,
    int LogicalProcessors,
    string Architecture,
    bool VirtualizationFirmwareEnabled);

/// <summary>Physical RAM module.</summary>
public sealed record MemoryModule(
    string Bank,
    string Manufacturer,
    string PartNumber,
    long CapacityBytes,
    double SpeedMHz,
    string FormFactor);

/// <summary>Aggregated memory information.</summary>
public sealed record MemoryInfo(
    long TotalBytes,
    long AvailableBytes,
    long CommittedBytes,
    IReadOnlyList<MemoryModule> Modules);

/// <summary>Motherboard / BIOS.</summary>
public sealed record MotherboardInfo(string Manufacturer, string Product, string SerialNumber, string Version);

public sealed record BiosInfo(string Manufacturer, string Version, DateTime? ReleaseDate, bool SecureBootCapable);

/// <summary>GPU device.</summary>
public sealed record GpuDeviceInfo(
    string Name,
    string AdapterRam,
    string DriverVersion,
    string DriverDate,
    string PnpDeviceId,
    bool IsDiscrete);

/// <summary>Storage drive.</summary>
public sealed record DriveInfo(
    string Model,
    string InterfaceType,
    string MediaType,
    long SizeBytes,
    string? SerialNumber,
    string? Firmware,
    IReadOnlyList<SmartAttribute> SmartAttributes);

/// <summary>Single SMART attribute (raw values reported honestly).</summary>
public sealed record SmartAttribute(
    int Id,
    string Name,
    ushort Current,
    ushort Worst,
    long RawValue,
    bool IsFailed,
    string? Interpretation);

/// <summary>Logical volume.</summary>
public sealed record VolumeInfo(
    string Mount,
    string Label,
    string FileSystem,
    long SizeBytes,
    long FreeBytes,
    string? DriveType);

/// <summary>Windows edition/build/activation snapshot.</summary>
public sealed record WindowsInfo(
    string ProductName,
    string Edition,
    string Version,
    string Build,
    string Architecture,
    string InstallDate,
    string? ActivationStatus,
    string Locale);

/// <summary>Complete hardware inventory produced by the Deep Scanner.</summary>
public sealed record HardwareInventory(
    CpuInfo Cpu,
    MemoryInfo Memory,
    MotherboardInfo Motherboard,
    BiosInfo Bios,
    IReadOnlyList<GpuDeviceInfo> Gpus,
    IReadOnlyList<DriveInfo> Drives,
    IReadOnlyList<VolumeInfo> Volumes,
    IReadOnlyList<DeviceEntry> Devices,
    string? TpmStatus,
    bool? SecureBootEnabled,
    string? BatteryStatus,
    IReadOnlyList<string> ProbeFailures);

/// <summary>Device Manager style device entry.</summary>
public sealed record DeviceEntry(
    string Name,
    string? Class,
    string? Manufacturer,
    string Status,
    string? ProblemCode,
    string PnpDeviceId);
