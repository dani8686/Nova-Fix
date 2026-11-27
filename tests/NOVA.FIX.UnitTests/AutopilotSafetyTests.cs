using NOVA.FIX.Autopilot;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;
using Xunit;

namespace NOVA.FIX.UnitTests;

public class AutopilotSafetyTests
{
    [Fact]
    public void MapFindingToAction_KnownFindingsHaveActions()
    {
        Assert.Equal("dism.checkhealth",
            AutopilotEngine.MapFindingToAction(Fake("find.servicing-errors")));
        Assert.Equal("update.inspect",
            AutopilotEngine.MapFindingToAction(Fake("find.update-failures")));
        Assert.Equal("storage.cleanup-temp",
            AutopilotEngine.MapFindingToAction(Fake("find.storage-pressure")));
        Assert.Equal("startup.disable",
            AutopilotEngine.MapFindingToAction(Fake("find.memory-pressure")));
    }

    [Fact]
    public void MapFindingToAction_UnknownFinding_ReturnsNull()
    {
        Assert.Null(AutopilotEngine.MapFindingToAction(Fake("find.unknown-thing")));
    }

    [Fact]
    public void ModeNotes_PromiseNoSilentActions()
    {
        // BuildPlanAsync notes are static — verify wording contract via reflection
        // on the private helper through public behavior instead:
        var safeNote = typeof(AutopilotEngine)
            .GetMethod("BuildModeNote", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static);
        Assert.NotNull(safeNote);

        var note = (string)safeNote!.Invoke(null, new object[] { AutopilotMode.Safe })!;
        Assert.Contains("without approval", note);

        var advanced = (string)safeNote.Invoke(null,
            new object[] { AutopilotMode.Advanced })!;
        Assert.Contains("explicit approval", advanced);
        Assert.Contains("never", advanced);
    }

    private static DiagnosticFinding Fake(string id) => new()
    {
        Id = id,
        Title = "t",
        Description = "d",
        Severity = FindingSeverity.Medium,
        AffectedComponent = "c",
        Confidence = ConfidenceLevel.Likely,
        Evidence = Array.Empty<EvidenceItem>(),
        PossibleCauses = Array.Empty<string>(),
        RecommendedSolution = "r",
        Risks = "none",
        VerificationMethod = "v",
    };
}
