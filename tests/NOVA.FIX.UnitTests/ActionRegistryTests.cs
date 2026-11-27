using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Repair.Actions;
using Xunit;

namespace NOVA.FIX.UnitTests;

/// <summary>The allowlist is the safety backbone of the repair system.</summary>
public class ActionRegistryTests
{
    private readonly ActionRegistry _registry = new();

    [Fact]
    public void Registry_ContainsCoreRepairActions()
    {
        foreach (var id in new[]
                 {
                     "dism.checkhealth", "dism.scanhealth", "dism.restorehealth",
                     "sfc.scannow", "update.reset-cache", "network.flush-dns",
                     "storage.cleanup-temp", "startup.disable",
                 })
        {
            Assert.True(_registry.IsAllowed(id), $"missing allowlisted action {id}");
        }
    }

    [Fact]
    public void EveryAction_HasFullSafetyMetadata()
    {
        foreach (var a in _registry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Target));
            Assert.False(string.IsNullOrWhiteSpace(a.Purpose));
            Assert.False(string.IsNullOrWhiteSpace(a.ExpectedResult));
            Assert.False(string.IsNullOrWhiteSpace(a.Verification));
            Assert.False(string.IsNullOrWhiteSpace(a.Rollback));
            Assert.NotEmpty(a.Preconditions);
            Assert.NotEmpty(a.AllowedVerbs);
            Assert.False(string.IsNullOrWhiteSpace(a.RequiredPermissions));
        }
    }

    [Fact]
    public void UnknownAction_IsRejected()
    {
        Assert.False(_registry.IsAllowed("rm.rf.slash"));
        Assert.False(_registry.IsAllowed("format-c"));
        Assert.False(_registry.IsAllowed(""));
        Assert.Null(_registry.Find("does-not-exist"));
    }

    [Fact]
    public void DestructiveActions_AreNotMarkedSafe()
    {
        var cleanup = _registry.Find("storage.cleanup-temp");
        Assert.NotNull(cleanup);
        Assert.True(cleanup!.Risk >= ActionRisk.Medium);
    }
}
