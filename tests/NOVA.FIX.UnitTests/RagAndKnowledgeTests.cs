using NOVA.FIX.AI.Rag;
using NOVA.FIX.Core.Logging;
using NOVA.FIX.Domain.Enums;
using NOVA.FIX.Domain.Models;
using Xunit;

namespace NOVA.FIX.UnitTests;

public class RagAndKnowledgeTests
{
    [Theory]
    [InlineData("windows update error", "\"windows\" OR \"update\" OR \"error\"")]
    [InlineData("DISM 0x800f0805", "\"DISM\" OR \"0x800f0805\"")]
    public void BuildMatchQuery_ProducesQuotedTerms(string input, string expected)
    {
        Assert.Equal(expected, SqliteKnowledgeBase.BuildMatchQuery(input));
    }

    [Fact]
    public void BuildMatchQuery_EmptyInput_IsSafe()
    {
        Assert.Equal("\"\"", SqliteKnowledgeBase.BuildMatchQuery("   "));
    }

    [Fact]
    public void BuildMatchQuery_StripsQuoteCharacters()
    {
        var q = SqliteKnowledgeBase.BuildMatchQuery("he said \"hello\" loudly");
        Assert.DoesNotContain("\"hello\" loudly", q);
        Assert.Contains("\"hello\"", q);
    }

    [Fact]
    public async Task KnowledgeBase_SeedsAndSearches_FullyOffline()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            $"novafix-kb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dbPath = Path.Combine(dir, "knowledge.db");
            var seedPath = Path.Combine(dir, "articles.json");
            await File.WriteAllTextAsync(seedPath, """
            [
              {
                "Id": "kb:test",
                "Title": "Test Article about DISM",
                "Category": "Test",
                "Content": "DISM RestoreHealth repairs the component store.",
                "Tags": "dism,repair",
                "Source": "unit-test",
                "Version": "1",
                "PublishedDate": "2026-11-26"
              }
            ]
            """);

            var kb = new SqliteKnowledgeBase(dbPath, seedPath, new NullLogger());
            await kb.InitializeAsync();
            var hits = await kb.SearchAsync("DISM repair", 5);

            Assert.NotEmpty(hits);
            Assert.Equal("kb:test", hits[0].Article.Id);
            Assert.NotEmpty(kb.DatabaseVersion);
            Assert.True(kb.DatabaseBuildDate > DateTime.MinValue);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RagService_WithoutLlm_ReturnsHonestFallback()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            $"novafix-rag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var kb = new SqliteKnowledgeBase(
                Path.Combine(dir, "k.db"),
                Path.Combine(dir, "missing-seed.json"),
                new NullLogger());
            await kb.InitializeAsync();

            var rag = new RagService(kb, new FakeLlm { Loaded = false },
                new NullLogger());
            var answer = await rag.AskAsync("something not in the db");

            Assert.False(answer.UsedLanguageModel);
            Assert.NotNull(answer.HonestFallbackNote);
            Assert.Equal(EvidenceBasis.ReferenceDocumentation, answer.PrimaryBasis);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class FakeLlm : NOVA.FIX.Domain.Contracts.ILocalLanguageModel
    {
        public bool Loaded { get; init; }
        public bool IsLoaded => Loaded;
        public string? ModelProfile => Loaded ? "fake" : null;
        public string? LoadError => Loaded ? null : "not loaded";
        public Task<bool> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(Loaded);
        public Task<string?> GenerateAsync(string prompt, int maxTokens = 512,
            CancellationToken ct = default)
            => Task.FromResult<string?>(Loaded ? "generated" : null);
        public void Unload() { }
    }

    private sealed class NullLogger : NOVA.FIX.Core.Logging.IAppLogger
    {
        public void Log(NOVA.FIX.Core.Logging.LogLevel level, string category,
            string message, Exception? exception = null) { }
        public IReadOnlyList<string> RecentEntries(int count) => Array.Empty<string>();
    }
}
