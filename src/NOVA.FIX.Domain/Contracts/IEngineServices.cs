using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Domain.Results;

namespace NOVA.FIX.Domain.Contracts;

/// <summary>Rule-based + AI diagnostic correlation engine.</summary>
public interface IDiagnosticEngine
{
    Task<IReadOnlyList<DiagnosticFinding>> AnalyzeAsync(
        IReadOnlyList<EvidenceItem> evidence,
        CancellationToken ct = default);
}

/// <summary>Allowlisted action registry — the only path to system modification.</summary>
public interface IActionRegistry
{
    IReadOnlyList<RepairActionDefinition> All { get; }
    RepairActionDefinition? Find(string actionId);
}

/// <summary>Definition of an allowlisted repair action.</summary>
public sealed record RepairActionDefinition(
    string Id,
    string DisplayName,
    string Category,
    string Target,
    string Purpose,
    string ExpectedResult,
    ActionRisk Risk,
    IReadOnlyList<string> Preconditions,
    string RequiredPermissions,
    string Verification,
    string Rollback,
    IReadOnlyList<string> AllowedVerbs);

/// <summary>Executes allowlisted actions only — never arbitrary commands.</summary>
public interface IRepairExecutor
{
    Task<OperationResult> ExecuteAsync(
        RepairProposal proposal,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken ct = default);
}

/// <summary>Local knowledge retrieval (RAG).</summary>
public interface IKnowledgeBase
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int maxResults = 5, CancellationToken ct = default);
    Task<KnowledgeArticle?> GetByIdAsync(string id, CancellationToken ct = default);
    string DatabaseVersion { get; }
    DateTime DatabaseBuildDate { get; }
}

/// <summary>Local language model abstraction — CPU-first, degrades gracefully.</summary>
public interface ILocalLanguageModel
{
    bool IsLoaded { get; }
    string? ModelProfile { get; }
    string? LoadError { get; }
    Task<bool> LoadAsync(CancellationToken ct = default);
    Task<string?> GenerateAsync(string prompt, int maxTokens = 512, CancellationToken ct = default);
    void Unload();
}
