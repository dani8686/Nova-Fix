using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;
using NOVA.FIX.Reporting;
using Xunit;

namespace NOVA.FIX.UnitTests;

public class ReportGeneratorTests
{
    private static ReportPayload Sample() => new(
        Metadata: new ReportMetadata(
            "NOVA FIX", "2.0.0", "not loaded", "1", "1",
            new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc),
            "TESTPC"),
        Hardware: null,
        Windows: new WindowsInfo("Windows 11 Pro", "Professional", "24H2",
            "26100", "x64", "2026-01-01", "Licensed", "en-US"),
        Findings: new List<DiagnosticFinding>
        {
            new()
            {
                Id = "f1", Title = "Low disk space",
                Description = "C: is nearly full.",
                Severity = FindingSeverity.High,
                AffectedComponent = "Storage",
                Confidence = ConfidenceLevel.Confirmed,
                Evidence = Array.Empty<EvidenceItem>(),
                PossibleCauses = new[] { "temp files" },
                RecommendedSolution = "Run Storage Guardian cleanup.",
                Risks = "None after review.",
                VerificationMethod = "Free space > 15%.",
            },
        },
        ActionsPerformed: Array.Empty<OperationResult>(),
        RemainingIssues: new[] { "Watch disk space" });

    [Fact]
    public void Sanitize_RemovesCreatorEmail()
    {
        var s = ReportGenerator.Sanitize(
            "contact daniel.865559@gmail.com for attribution");
        Assert.DoesNotContain("daniel.865559@gmail.com", s);
        Assert.Contains("[attribution-redacted]", s);
    }

    [Fact]
    public void Sanitize_RedactsPasswords()
    {
        var s = ReportGenerator.Sanitize("password: hunter2secret");
        Assert.DoesNotContain("hunter2secret", s);
        Assert.Contains("[redacted]", s);
    }

    [Fact]
    public void Sanitize_MasksSerialNumbers()
    {
        var s = ReportGenerator.Sanitize("Serial: ABCDEFGH1234");
        Assert.DoesNotContain("ABCDEFGH1234", s);
        Assert.Contains("****", s);
    }

    [Theory]
    [InlineData(ReportFormat.Txt, "txt")]
    [InlineData(ReportFormat.Html, "html")]
    [InlineData(ReportFormat.Json, "json")]
    [InlineData(ReportFormat.Csv, "csv")]
    public void Generate_WritesLocalFile(ReportFormat format, string ext)
    {
        var dir = Path.Combine(Path.GetTempPath(),
            $"novafix-report-{Guid.NewGuid():N}");
        try
        {
            var gen = new ReportGenerator();
            var file = gen.Generate(Sample(), format, dir);
            Assert.True(File.Exists(file));
            Assert.EndsWith("." + ext, file, StringComparison.OrdinalIgnoreCase);
            var content = File.ReadAllText(file);
            Assert.Contains("NOVA FIX", content);
            Assert.DoesNotContain("daniel.865559@gmail.com", content);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
