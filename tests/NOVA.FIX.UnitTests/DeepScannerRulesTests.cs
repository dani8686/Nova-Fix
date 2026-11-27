using NOVA.FIX.AI.Correlation;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Contracts;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;
using Xunit;

namespace NOVA.FIX.UnitTests;

public class DeepScannerRulesTests
{
    [Fact]
    public async Task DeviceProblems_ProduceFinding()
    {
        var correlator = new EvidenceCorrelator(
            new FakeKb(), new FakeLlm(), new NullLogger(),
            allowAiInterpretation: false);

        var evidence = new List<EvidenceItem>
        {
            new("device-manager",
                "Intel Wi-Fi adapter — error 10 (failed to start)",
                EvidenceBasis.SystemEvidence),
        };

        var findings = await correlator.AnalyzeAsync(evidence);
        Assert.Contains(findings, f => f.Id == "find.device-errors");
    }

    [Fact]
    public async Task MicrosoftServiceHighCpu_IsNotFlaggedAsProblematic()
    {
        var correlator = new EvidenceCorrelator(
            new FakeKb(), new FakeLlm(), new NullLogger(),
            allowAiInterpretation: false);

        var evidence = new List<EvidenceItem>
        {
            new("service:SysMain",
                "SysMain is a Microsoft service using resources (CPU, memory)",
                EvidenceBasis.SystemEvidence),
        };

        var findings = await correlator.AnalyzeAsync(evidence);
        Assert.DoesNotContain(findings, f =>
            f.Title.Contains("SysMain", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LowDiskSpace_ProducesHighSeverityFinding()
    {
        var correlator = new EvidenceCorrelator(
            new FakeKb(), new FakeLlm(), new NullLogger(),
            allowAiInterpretation: false);

        var evidence = new List<EvidenceItem>
        {
            new("storage",
                "Low free space on C:\\: 3.0 GB free of 100.0 GB",
                EvidenceBasis.SystemEvidence),
        };

        var findings = await correlator.AnalyzeAsync(evidence);
        var f = Assert.Single(findings, x => x.Id == "find.storage-pressure");
        Assert.Equal(FindingSeverity.High, f.Severity);
        Assert.Equal(ConfidenceLevel.Confirmed, f.Confidence);
        Assert.False(f.IsAiInterpreted);
    }

    [Fact]
    public async Task EvidenceBasis_IsNeverLlmWhenLlmDisabled()
    {
        var correlator = new EvidenceCorrelator(
            new FakeKb(), new FakeLlm { Loaded = true }, new NullLogger(),
            allowAiInterpretation: false);

        var findings = await correlator.AnalyzeAsync(new List<EvidenceItem>
        {
            new("probe:cpu", "CPU sample", EvidenceBasis.SystemEvidence),
        });

        Assert.All(findings, f => Assert.False(f.IsAiInterpreted));
    }

    private sealed class FakeKb : IKnowledgeBase
    {
        public string DatabaseVersion => "1";
        public DateTime DatabaseBuildDate => new(2026, 11, 26);
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query,
            int maxResults = 5, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KnowledgeHit>>(Array.Empty<KnowledgeHit>());
        public Task<KnowledgeArticle?> GetByIdAsync(string id,
            CancellationToken ct = default)
            => Task.FromResult<KnowledgeArticle?>(null);
    }

    private sealed class FakeLlm : ILocalLanguageModel
    {
        public bool Loaded { get; init; }
        public bool IsLoaded => Loaded;
        public string? ModelProfile => "fake";
        public string? LoadError => null;
        public Task<bool> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(Loaded);
        public Task<string?> GenerateAsync(string prompt, int maxTokens = 512,
            CancellationToken ct = default)
            => Task.FromResult<string?>(Loaded ? "{}" : null);
        public void Unload() { }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Log(LogLevel level, string category, string message,
            Exception? exception = null) { }
        public IReadOnlyList<string> RecentEntries(int count) => Array.Empty<string>();
    }
}
