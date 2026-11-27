using NOVA.FIX.App;
using NOVA.FIX.App.ViewModels;
using NOVA.FIX.Core.Branding;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Core.Services;
using Xunit;

namespace NOVA.FIX.UITests;

/// <summary>
/// UI view-model tests: navigation, About attribution, settings. These run
/// without showing windows. Full click-through UI automation runs on a Windows
/// desktop session via scripts/ui-automation.ps1.
/// </summary>
public class ShellViewModelTests
{
    private static ServiceContainer BuildContainer()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            $"novafix-ui-{Guid.NewGuid():N}");
        var paths = new AppPaths(dir);
        var settings = new SettingsService(paths);
        settings.LoadAsync().GetAwaiter().GetResult();

        return AppBootstrap.BuildContainer(paths, settings, new NoopLogger());
    }

    [Fact]
    public void Navigate_SwitchesPages()
    {
        var vm = new ShellViewModel(BuildContainer());
        Assert.NotNull(vm.CurrentPage);

        vm.NavigateCommand.Execute("scanner");
        Assert.Equal("Deep Scanner", vm.CurrentHeader);

        vm.NavigateCommand.Execute("settings");
        Assert.Equal("Settings & About", vm.CurrentHeader);
    }

    [Fact]
    public void UnknownNavigation_IsIgnoredSafely()
    {
        var vm = new ShellViewModel(BuildContainer());
        var before = vm.CurrentHeader;
        vm.NavigateCommand.Execute("no-such-page");
        Assert.Equal(before, vm.CurrentHeader);
    }

    [Fact]
    public void AboutPage_ExposesCreatorAttribution()
    {
        var vm = new ShellViewModel(BuildContainer());
        vm.NavigateCommand.Execute("settings");
        var settings = Assert.IsType<SettingsViewModel>(vm.CurrentPage);

        Assert.Equal(CreatorInformation.ProductName, settings.AboutProduct);
        Assert.Equal("Danial Mousavi", settings.AboutCreator);
        Assert.Equal("@dani.m7016", settings.AboutInstagram);
        Assert.Equal("daniel.865559@gmail.com", settings.AboutEmail);
        Assert.Contains("Danial Mousavi", settings.AboutAttribution);
    }

    [Fact]
    public void Sidebar_DisplaysCreatorLine()
    {
        var vm = new ShellViewModel(BuildContainer());
        Assert.Contains("Danial Mousavi", vm.CreatorLine);
        Assert.Contains("@dani.m7016", vm.CreatorLine);
    }

    [Fact]
    public void AllSixteenModules_CanBeNavigated()
    {
        var vm = new ShellViewModel(BuildContainer());
        foreach (var key in new[]
                 {
                     "overview", "scanner", "detective", "repair",
                     "performance", "startup", "services", "drivers",
                     "storage", "compatibility", "rescue", "reports",
                     "autopilot", "updates", "ai", "settings",
                 })
        {
            vm.NavigateCommand.Execute(key);
            Assert.NotNull(vm.CurrentPage);
            Assert.False(string.IsNullOrWhiteSpace(vm.CurrentHeader));
        }
    }

    private sealed class NoopLogger : IAppLogger
    {
        public void Log(LogLevel level, string category, string message,
            Exception? exception = null)
        {
        }

        public IReadOnlyList<string> RecentEntries(int count) => Array.Empty<string>();
    }
}
