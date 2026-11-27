namespace NOVA.FIX.Domain.Models;

/// <summary>Local knowledge article used by the RAG engine.</summary>
public sealed record KnowledgeArticle(
    string Id,
    string Title,
    string Category,
    string Content,
    IReadOnlyList<string> Tags,
    string Source,
    string Version,
    DateTime? PublishedDate);

/// <summary>Search hit returned by the local retrieval engine.</summary>
public sealed record KnowledgeHit(
    KnowledgeArticle Article,
    double Score,
    string Snippet,
    int StartOffset,
    int Length);

/// <summary>RAG answer with explicit provenance — facts vs hypotheses stay separated.</summary>
public sealed record RagAnswer(
    string Question,
    string Answer,
    IReadOnlyList<KnowledgeHit> Citations,
    IReadOnlyList<EvidenceItem> SupportingEvidence,
    EvidenceBasis PrimaryBasis,
    bool UsedLanguageModel,
    string? ModelProfile,
    string? HonestFallbackNote);
