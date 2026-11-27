using NOVA.FIX.Repair.Workflows;
using Xunit;

namespace NOVA.FIX.UnitTests;

/// <summary>Deletion gate: only approved temp/cache paths may ever be deleted.</summary>
public class DeletionSafetyTests
{
    [Fact]
    public void WindowsRoot_IsRejected()
    {
        var (ok, err) = ActionVerbs.ValidateDeletionTarget(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Assert.False(ok);
        Assert.NotNull(err);
    }

    [Fact]
    public void UserProfile_IsRejected()
    {
        var (ok, _) = ActionVerbs.ValidateDeletionTarget(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Assert.False(ok);
    }

    [Fact]
    public void UserDocuments_AreRejected()
    {
        var (ok, _) = ActionVerbs.ValidateDeletionTarget(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Assert.False(ok);
    }

    [Fact]
    public void PathRoot_IsRejected()
    {
        var root = Path.GetPathRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;
        var (ok, _) = ActionVerbs.ValidateDeletionTarget(root);
        Assert.False(ok);
    }

    [Fact]
    public void UserTemp_IsAllowed()
    {
        var tempFile = Path.Combine(Path.GetTempPath(),
            $"novafix-test-{Guid.NewGuid():N}.tmp");
        var (ok, err) = ActionVerbs.ValidateDeletionTarget(tempFile);
        Assert.True(ok, err);
    }

    [Fact]
    public void EmptyPath_IsRejected()
    {
        var (ok, _) = ActionVerbs.ValidateDeletionTarget("");
        Assert.False(ok);
    }

    [Fact]
    public void GarbagePath_IsRejected()
    {
        var (ok, _) = ActionVerbs.ValidateDeletionTarget("<<<not a path>>>");
        Assert.False(ok);
    }
}
