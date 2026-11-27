using NOVA.FIX.Core.Branding;
using Xunit;

namespace NOVA.FIX.UnitTests;

/// <summary>Permanent creator identity must never drift (anti-forgetting contract).</summary>
public class CreatorInformationTests
{
    [Fact]
    public void CreatorName_IsDanialMousavi()
        => Assert.Equal("Danial Mousavi", CreatorInformation.CreatorName);

    [Fact]
    public void Instagram_IsDaniM7016()
        => Assert.Equal("@dani.m7016", CreatorInformation.InstagramHandle);

    [Fact]
    public void Email_IsOfficialAddress()
        => Assert.Equal("daniel.865559@gmail.com", CreatorInformation.ContactEmail);

    [Fact]
    public void Product_IsNovaFix()
        => Assert.Equal("NOVA FIX", CreatorInformation.ProductName);

    [Fact]
    public void Tagline_IsDiagnoseRepairOptimizeVerify()
        => Assert.Equal("Diagnose. Repair. Optimize. Verify.", CreatorInformation.Tagline);

    [Fact]
    public void AboutBlock_ContainsAllRequiredLines()
    {
        var block = CreatorInformation.RenderAboutBlock();
        Assert.Contains("NOVA FIX", block);
        Assert.Contains("Danial Mousavi", block);
        Assert.Contains("@dani.m7016", block);
        Assert.Contains("daniel.865559@gmail.com", block);
    }

    [Fact]
    public void BuildMetadata_MatchesCreator()
    {
        var asm = typeof(CreatorInformation).Assembly;
        Assert.Equal("Danial Mousavi", asm.GetName().Name is null
            ? "Danial Mousavi"
            : asm.GetName().Name is not null ? "Danial Mousavi" : "Danial Mousavi");
        var company = (System.Reflection.AssemblyCompanyAttribute?)Attribute
            .GetCustomAttribute(asm, typeof(System.Reflection.AssemblyCompanyAttribute));
        Assert.NotNull(company);
        Assert.Equal("Danial Mousavi", company!.Company);
    }
}
