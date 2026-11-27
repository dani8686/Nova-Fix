using NOVA.FIX.Domain.Results;
using NOVA.FIX.Domain.Enums;

namespace NOVA.FIX.Domain.Models;

/// <summary>Restore point / snapshot record for Rescue &amp; Restore.</summary>
public sealed record RestorePointEntry(
    string Id,
    string Name,
    DateTime Created,
    string Description,
    string Type);

/// <summary>Configuration snapshot captured before modifications.</summary>
public sealed record ConfigurationSnapshot(
    string Id,
    string Kind,
    DateTime Created,
    string FilePath,
    string Description,
    long SizeBytes);

/// <summary>Single reversible change recorded in the change journal.</summary>
public sealed record ChangeRecord(
    string Id,
    DateTime Timestamp,
    string Module,
    string Target,
    string Description,
    string BeforeState,
    string AfterState,
    string? RollbackData,
    bool RolledBack);

/// <summary>Local update package (Offline Update Manager).</summary>
public sealed record UpdatePackageInfo(
    string FileName,
    string PackageKind,
    string? Version,
    string? TargetComponent,
    long SizeBytes,
    string Sha256,
    string SignatureStatus,
    string? Signer,
    string CompatibilityStatus,
    string? Notes);

/// <summary>Update installation history row.</summary>
public sealed record UpdateInstallRecord(
    string Id,
    DateTime Timestamp,
    string FileName,
    string PackageKind,
    string? Version,
    string Result,
    string? Detail);

/// <summary>Report metadata shared by all report formats.</summary>
public sealed record ReportMetadata(
    string GeneratedBy,
    string ProductVersion,
    string AiModelVersion,
    string KnowledgeDbVersion,
    string RulesDbVersion,
    DateTime GeneratedAt,
    string MachineAlias);

/// <summary>Full in-memory report payload.</summary>
public sealed record ReportPayload(
    ReportMetadata Metadata,
    HardwareInventory? Hardware,
    WindowsInfo? Windows,
    IReadOnlyList<DiagnosticFinding> Findings,
    IReadOnlyList<OperationResult> ActionsPerformed,
    IReadOnlyList<string> RemainingIssues);
