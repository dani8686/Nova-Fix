using NOVA.FIX.Core.Logging;
using NOVA.FIX.Optimization;
using Xunit;

namespace NOVA.FIX.UnitTests;

/// <summary>Critical services must be protected by both managers.</summary>
public class ServiceCriticalListTests
{
    [Theory]
    [InlineData("RpcSs")]
    [InlineData("DcomLaunch")]
    [InlineData("LanmanServer")]
    [InlineData("Dnscache")]
    [InlineData("EventLog")]
    [InlineData("WinDefend")]
    [InlineData("CryptSvc")]
    public void CriticalServices_AreProtected(string serviceName)
    {
        var sm = new ServiceManager(new NullLogger());
        Assert.True(sm.IsCritical(serviceName));
    }

    [Theory]
    [InlineData("RemoteRegistry")]
    [InlineData("SomeThirdPartySvc")]
    public void NonCriticalServices_AreModifiable(string serviceName)
    {
        var sm = new ServiceManager(new NullLogger());
        Assert.False(sm.IsCritical(serviceName));
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Log(LogLevel level, string category, string message,
            Exception? exception = null)
        {
        }

        public IReadOnlyList<string> RecentEntries(int count) =>
            Array.Empty<string>();
    }
}
