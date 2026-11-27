using NOVA.FIX.Drivers;
using Xunit;

namespace NOVA.FIX.UnitTests;

public class DeletionAndVersionTests
{
    [Theory]
    [InlineData("31.0.15.4601", "31.0.15.4601", 0)]
    [InlineData("31.0.15.4601", "31.0.15.4500", 1)]
    [InlineData("30.0.15.4601", "31.0.15.4601", -1)]
    public void DriverVersions_CompareCorrectly(string a, string b, int expectedSign)
    {
        var cmp = DriverIntelligence.CompareVersionsSafe(a, b);
        Assert.Equal(expectedSign, Math.Sign(cmp));
    }

    [Fact]
    public void NullVersions_DoNotThrow()
    {
        var cmp = DriverIntelligence.CompareVersionsSafe(null, "1.0");
        Assert.True(cmp <= 0);
    }

    [Fact]
    public void InfParse_DetectsHardwareIds()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            $"novafix-inf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var inf = Path.Combine(dir, "test.inf");
            File.WriteAllText(inf, """
                [Version]
                Signature="$Windows NT$"
                Class=Display
                ClassGuid={4d36e968-e325-11ce-bfc1-08002be10318}
                Provider=%ProviderName%
                DriverVer=11/26/2026,31.0.15.4601

                [Manufacturer]
                %ProviderName%=Models,NTamd64

                [Models.NTamd64]
                %DeviceName%=Install, PCI\VEN_8086&DEV_5916
                """);
            var info = DriverIntelligence.ParseInf(inf);
            Assert.Equal("Display", info.Class);
            Assert.Equal("31.0.15.4601", info.DriverVer);
            Assert.Contains(info.HardwareIds, h => h.Contains("VEN_8086"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
