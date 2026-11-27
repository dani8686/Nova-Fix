using NOVA.FIX.Localization;
using Xunit;

namespace NOVA.FIX.UnitTests;

public class LocalizationTests
{
    [Fact]
    public void English_ContainsCoreKeys()
    {
        var loc = new LocalizationService(Path.Combine("nonexistent"));
        loc.Language = "en";
        Assert.Equal("System Overview", loc["nav.overview"]);
        Assert.Equal("Ready", loc["status.ready"]);
    }

    [Fact]
    public void Persian_TranslatesCoreKeys()
    {
        var loc = new LocalizationService(Path.Combine("nonexistent"));
        loc.Language = "fa";
        Assert.Equal("نمای کلی سیستم", loc["nav.overview"]);
        Assert.Equal("آماده", loc["status.ready"]);
    }

    [Fact]
    public void Persian_IsRightToLeft()
    {
        var loc = new LocalizationService(Path.Combine("nonexistent"));
        loc.Language = "fa";
        Assert.True(loc.IsRightToLeft);
        Assert.Equal("RightToLeft", loc.FlowDirection);
        loc.Language = "en";
        Assert.False(loc.IsRightToLeft);
    }

    [Fact]
    public void MissingKey_ReturnsKeyNotCrash()
    {
        var loc = new LocalizationService(Path.Combine("nonexistent"));
        Assert.Equal("does.not.exist", loc["does.not.exist"]);
    }

    [Fact]
    public void UnknownLanguage_FallsBackToEnglish()
    {
        var loc = new LocalizationService(Path.Combine("nonexistent"));
        loc.Language = "de";
        Assert.Equal("en", loc.Language);
    }
}
