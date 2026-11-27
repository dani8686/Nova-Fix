using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;

namespace NOVA.FIX.AI.Rag;

/// <summary>
/// Offline retrieval-augmented generation: local FTS retrieval + optional local
/// LLM synthesis. Every answer states its basis and cites bundled sources.
/// When the LLM is unavailable the service returns retrieved passages verbatim
/// and says so — it never pretends a retrieval snippet is a generated answer.
/// </summary>
public sealed class RagService
{
    private readonly IKnowledgeBase _knowledge;
    private readonly ILocalLanguageModel? _llm;
    private readonly IAppLogger _logger;

    public RagService(IKnowledgeBase knowledge, ILocalLanguageModel? llm, IAppLogger logger)
    {
        _knowledge = knowledge;
        _llm = llm;
        _logger = logger;
    }

    public async Task<RagAnswer> AskAsync(
        string question,
        IReadOnlyList<EvidenceItem>? systemEvidence = null,
        CancellationToken ct = default)
    {
        await _knowledge.InitializeAsync(ct).ConfigureAwait(false);

        var hits = await _knowledge.SearchAsync(question, 5, ct).ConfigureAwait(false);
        var citations = hits.Select(h => h.Article).Distinct().ToList();

        if (citations.Count == 0)
        {
            return new RagAnswer(
                Question: question,
                Answer: "No matching entry was found in the local knowledge base. " +
                        "This question cannot be answered offline with the bundled data.",
                Citations: Array.Empty<KnowledgeHit>(),
                SupportingEvidence: systemEvidence ?? Array.Empty<EvidenceItem>(),
                PrimaryBasis: EvidenceBasis.ReferenceDocumentation,
                UsedLanguageModel: false,
                ModelProfile: null,
                HonestFallbackNote: "No local knowledge matched the query.");
        }

        var contextBlock = string.Join("\n\n",
            citations.Take(3).Select(c =>
                $"[{c.Id}] {c.Title} ({c.Category}):\n{Truncate(c.Content, 1200)}"));

        if (_llm is { IsLoaded: true })
        {
            var evidenceBlock = systemEvidence is { Count: > 0 }
                ? "\n\nSYSTEM EVIDENCE:\n" + string.Join("\n",
                    systemEvidence.Take(10).Select(e => $"- {e.Detail}"))
                : string.Empty;

            var prompt =
                "You are NOVA FIX, an offline Windows diagnostics assistant. " +
                "Answer ONLY from the reference material and evidence below. " +
                "Cite reference ids like [kb:...] where used. If the material does " +
                "not contain the answer, say so honestly.\n\n" +
                $"REFERENCE:\n{contextBlock}{evidenceBlock}\n\nQUESTION: {question}\nANSWER:";

            var generated = await _llm.GenerateAsync(prompt, 480, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(generated))
            {
                return new RagAnswer(
                    Question: question,
                    Answer: generated,
                    Citations: hits,
                    SupportingEvidence: systemEvidence ?? Array.Empty<EvidenceItem>(),
                    PrimaryBasis: EvidenceBasis.ProbabilisticInterpretation,
                    UsedLanguageModel: true,
                    ModelProfile: _llm.ModelProfile,
                    HonestFallbackNote: null);
            }

            _logger.Log(LogLevel.Warn, "Rag",
                "LLM returned no output; degrading to retrieved passages.");
        }

        // Honest fallback: retrieved passages, clearly labelled.
        var passage = string.Join("\n\n", hits.Take(3).Select(h =>
            $"• {h.Article.Title}: {Truncate(h.Article.Content, 500)}"));

        return new RagAnswer(
            Question: question,
            Answer: passage,
            Citations: hits,
            SupportingEvidence: systemEvidence ?? Array.Empty<EvidenceItem>(),
            PrimaryBasis: EvidenceBasis.ReferenceDocumentation,
            UsedLanguageModel: false,
            ModelProfile: _llm?.ModelProfile,
            HonestFallbackNote:
                "The local language model is unavailable; showing retrieved " +
                "reference passages directly.");
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}
