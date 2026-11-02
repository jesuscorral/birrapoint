namespace BirraPoint.Api.UnitTests.Architecture;

public class ModuleBoundaryScannerTests
{
    private static readonly Dictionary<string, string> DbSets = new(StringComparer.Ordinal)
    {
        ["BeerEntries"] = "Organization",
        ["Judges"] = "Organization",
        ["Evaluations"] = "Judging",
    };

    private static List<Violation> Scan(string source, string? ownModule = "Judging") =>
        ModuleBoundaryScanner.Scan("Modules/Judging/X.cs", source, ownModule, DbSets).ToList();

    [Fact]
    public void PlainUsingOfForeignModuleIsFlagged()
    {
        var violation = Assert.Single(Scan("using BirraPoint.Api.Modules.Organization.Domain;"));

        Assert.Equal("Organization", violation.Module);
        Assert.Contains("using:Domain", violation.Members);
    }

    [Theory]
    [InlineData("using static BirraPoint.Api.Modules.Organization.Domain.Rules;")]
    [InlineData("global using BirraPoint.Api.Modules.Organization.Domain;")]
    [InlineData("using Org = BirraPoint.Api.Modules.Organization.Domain;")]
    [InlineData("using Competition = BirraPoint.Api.Modules.Organization.Domain.Competition;")]
    public void UsingVariantsOfForeignModuleAreFlagged(string source)
    {
        var violation = Assert.Single(Scan(source));

        Assert.Equal("Organization", violation.Module);
    }

    [Fact]
    public void ContractsUsingAndAliasAreAllowed()
    {
        Assert.Empty(Scan("using BirraPoint.Api.Modules.Organization.Contracts;"));
        Assert.Empty(Scan("using Dtos = BirraPoint.Api.Modules.Organization.Contracts.Dtos;"));
    }

    [Fact]
    public void OwnModuleUsingIsAllowed() =>
        Assert.Empty(Scan("using BirraPoint.Api.Modules.Judging.Domain;"));

    [Fact]
    public void FullyQualifiedForeignNameInCodeIsFlagged()
    {
        var violation = Assert.Single(Scan("var x = new BirraPoint.Api.Modules.Organization.Domain.BeerEntry();"));

        Assert.Equal("Organization", violation.Module);
        Assert.Contains("fqn:Domain.BeerEntry", violation.Members);
    }

    [Fact]
    public void FullyQualifiedGlobalForeignNameIsFlagged() =>
        Assert.Single(Scan("global::BirraPoint.Api.Modules.Organization.Domain.BeerEntry e;"));

    [Fact]
    public void FullyQualifiedContractsNameIsAllowed() =>
        Assert.Empty(Scan("BirraPoint.Api.Modules.Organization.Contracts.IJudgeDirectory d;"));

    [Fact]
    public void FullyQualifiedNameInCommentIsIgnored() =>
        Assert.Empty(Scan("// see BirraPoint.Api.Modules.Organization.Domain.BeerEntry\n/* BirraPoint.Api.Modules.Organization.Domain.X */"));

    [Fact]
    public void NamespaceDeclarationIsNotAReference() =>
        Assert.Empty(Scan("namespace BirraPoint.Api.Modules.Organization.Domain;"));

    [Theory]
    [InlineData("ctx.BeerEntries")]
    [InlineData("_db.BeerEntries")]
    [InlineData("appDbContext.BeerEntries")]
    [InlineData("this.dbContext.BeerEntries")]
    [InlineData("dbContext.BeerEntries")]
    [InlineData("_context.BeerEntries")]
    public void DbSetOfForeignModuleOnContextReceiverIsFlagged(string access)
    {
        var violation = Assert.Single(Scan($"var q = {access}.Where(e => true);"));

        Assert.Equal("Organization", violation.Module);
        Assert.Contains("BeerEntries", violation.Members);
    }

    [Fact]
    public void DbSetOfOwnModuleIsAllowed() =>
        Assert.Empty(Scan("var q = ctx.Evaluations;"));

    [Theory]
    [InlineData("table.Judges")]
    [InlineData("entry.BeerEntries")]
    [InlineData("competition.Judges.Count")]
    public void NavigationsWithSameNameAreNotFlagged(string access) =>
        Assert.Empty(Scan($"var n = {access};"));
}
