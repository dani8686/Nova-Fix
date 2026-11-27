using NOVA.FIX.Domain.Enums;

namespace NOVA.FIX.Domain.Models;

/// <summary>A single piece of evidence backing a diagnostic finding.</summary>
public sealed record EvidenceItem(
    string Source,
    string Detail,
    EvidenceBasis Basis,
    DateTime? Timestamp = null,
    string? Reference = null);

/// <summary>
/// Evidence-driven problem produced by the AI Problem Detective.
/// Every field is filled from real collected data — uncertainty is always explained.
/// </summary>
public sealed class DiagnosticFinding
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required FindingSeverity Severity { get; init; }
    public required string AffectedComponent { get; init; }
    public required ConfidenceLevel Confidence { get; init; }
    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }
    public required IReadOnlyList<string> PossibleCauses { get; init; }
    public required string RecommendedSolution { get; init; }
    public required string Risks { get; init; }
    public required string VerificationMethod { get; init; }
    public string? KnowledgeCitation { get; init; }
    public bool IsAiInterpreted { get; init; }
    public string? UncertaintyExplanation { get; init; }
}

/// <summary>Structured AI repair proposal — the AI proposes, the execution engine disposes.</summary>
public sealed class RepairProposal
{
    public required string Id { get; init; }
    public required string ActionId { get; init; }
    public required string Target { get; init; }
    public required string Purpose { get; init; }
    public required string ExpectedResult { get; init; }
    public required ActionRisk Risk { get; init; }
    public required IReadOnlyList<string> Preconditions { get; init; }
    public required string RequiredPermissions { get; init; }
    public required string Verification { get; init; }
    public required string Rollback { get; init; }
    public string? FindingId { get; init; }
    public bool RequiresUserApproval { get; init; } = true;
}
