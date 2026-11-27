using NOVA.FIX.Core.Logging;
using NOVA.FIX.Updates;
using Xunit;

namespace NOVA.FIX.UnitTests;

public class OfflineUpdateManagerTests
{
    [Theory]
    [InlineData("patch.msu", "WindowsUpdate")]
    [InlineData("driver.inf", "Driver")]
    [InlineData("setup.exe", "Application")]
    [InlineData("knowledge-pack.zip", "KnowledgeBase")]
    [InlineData("model.gguf.zip", "AiModel")]
    public void Classify_ByExtensionAndName(string file, string expected)
    {
        var kind = OfflineUpdateManager.Classify(
            Path.Combine(@"C:\updates", file),
            Path.GetExtension(file));
        Assert.Equal(expected, kind);
    }

    [Fact]
    public async Task Import_MissingFile_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            $"novafix-upd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var mgr = new OfflineUpdateManager(
                new NullLogger(), dir, Path.Combine(dir, "h.db"), "2.0.0");
            var result = await mgr.ImportAsync(Path.Combine(dir, "nope.zip"));
            Assert.Null(result);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Import_ZipWithManifest_ReadsVersion()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            $"novafix-upd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var zipPath = Path.Combine(dir, "knowledge-pack.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(
                       zipPath, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("manifest.json");
                using var w = new StreamWriter(entry.Open());
                w.Write("""{"version":"2.1.0","component":"KnowledgeBase","notes":"test"}""");
            }

            var mgr = new OfflineUpdateManager(
                new NullLogger(), dir, Path.Combine(dir, "h.db"), "2.0.0");
            var info = await mgr.ImportAsync(zipPath);

            Assert.NotNull(info);
            Assert.Equal("2.1.0", info!.Version);
            Assert.Equal("KnowledgeBase", info.TargetComponent);
            Assert.False(string.IsNullOrEmpty(info.Sha256));
            Assert.StartsWith("Compatible", info.CompatibilityStatus);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Install_WithoutApproval_IsRecordedAsRejected()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            $"novafix-upd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var mgr = new OfflineUpdateManager(
                new NullLogger(), dir, Path.Combine(dir, "h.db"), "2.0.0");
            var pkg = new NOVA.FIX.Domain.Models.UpdatePackageInfo(
                "x.zip", "KnowledgeBase", "2.1.0", "KnowledgeBase",
                100, "ABC", "Unsigned", null, "Compatible", null);

            var record = await mgr.InstallAsync(pkg, userApproved: false);
            Assert.Equal("Rejected", record.Result);

            var history = await mgr.HistoryAsync();
            Assert.Single(history);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Log(LogLevel level, string category, string message,
            Exception? exception = null) { }
        public IReadOnlyList<string> RecentEntries(int count) => Array.Empty<string>();
    }
}
