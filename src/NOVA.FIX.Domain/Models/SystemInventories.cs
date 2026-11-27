namespace NOVA.FIX.Domain.Models;

/// <summary>Windows service entry for the Service Manager.</summary>
public sealed record ServiceEntry(
    string ServiceName,
    string DisplayName,
    string Description,
    string Status,
    string StartType,
    string? ExecutablePath,
    string? Publisher,
    string? LogonAccount,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> DependentServices,
    IReadOnlyList<string> FailureHistory)
{
    /// <summary>Display helper for grid bindings.</summary>
    public string DependenciesString => Dependencies.Count == 0
        ? "—"
        : string.Join(", ", Dependencies);
}

/// <summary>Startup entry (Run key, Startup folder, or scheduled task).</summary>
public sealed record StartupEntry(
    string Id,
    string Name,
    string? Publisher,
    string Path,
    string SignatureStatus,
    string Location,
    string Status,
    string Classification,
    string Evidence);

/// <summary>Installed driver / device-driver binding.</summary>
public sealed record DriverEntry(
    string DeviceName,
    string HardwareId,
    string? DriverVersion,
    string? DriverDate,
    string? Provider,
    string? InfName,
    string? Signer,
    string Status,
    string PnpDeviceId,
    bool IsBoxed);

/// <summary>Driver package available in the local repository.</summary>
public sealed record DriverPackage(
    string Id,
    string Name,
    string Version,
    string? HardwareId,
    string SourcePath,
    string SignatureStatus,
    string? SignedBy,
    string? Date);

/// <summary>Installed application record.</summary>
public sealed record InstalledApplication(
    string Name,
    string Version,
    string? Publisher,
    string InstallLocation,
    string UninstallString,
    string? EstimatedSize,
    string SignatureStatus);

/// <summary>Scheduled task record.</summary>
public sealed record ScheduledTaskEntry(
    string TaskName,
    string TaskPath,
    string State,
    string? Author,
    string? LastRunTime,
    string? NextRunTime,
    string ActionsSummary);
