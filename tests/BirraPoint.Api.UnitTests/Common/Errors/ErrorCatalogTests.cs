using System.Globalization;
using System.Text.RegularExpressions;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Modules.Judging.Errors;
using BirraPoint.Api.Modules.Organization.Errors;

namespace BirraPoint.Api.UnitTests.Common.Errors;

/// <summary>
/// Principle VI closed list: the union of the host and module catalogs is exactly the types in
/// contracts/rest-api.md §Error catalog, and a URN owned by several modules (a shared wire contract,
/// ADR-0033) carries the same status and title everywhere.
/// </summary>
public sealed partial class ErrorCatalogTests
{
    private const string UrnPrefix = "urn:birrapoint:";

    private static readonly (string Source, IReadOnlyList<(string Urn, int Status, string Title)> Entries)[] Catalogs =
    [
        (nameof(HostErrorCatalog), [.. HostErrorCatalog.Entries.Values]),
        (nameof(OrganizationErrorCatalog), [.. OrganizationErrorCatalog.Entries.Values]),
        (nameof(JudgingErrorCatalog), [.. JudgingErrorCatalog.Entries.Values]),
    ];

    [Fact]
    public void UnionOfCatalogsEqualsTheContractErrorCatalog()
    {
        var contract = ReadContractErrors();
        var implemented = Catalogs.SelectMany(c => c.Entries).Select(e => (e.Urn, e.Status)).Distinct().ToList();

        Assert.Equal(15, contract.Count);
        Assert.Equal(
            contract.OrderBy(e => e.Urn, StringComparer.Ordinal),
            implemented.OrderBy(e => e.Urn, StringComparer.Ordinal));
    }

    [Fact]
    public void ADuplicatedUrnHasIdenticalStatusAndTitleInEveryCatalog()
    {
        var duplicated = Catalogs
            .SelectMany(c => c.Entries.Select(e => (c.Source, Entry: e)))
            .GroupBy(x => x.Entry.Urn, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.NotEmpty(duplicated);
        foreach (var group in duplicated)
        {
            Assert.Single(group.Select(x => (x.Entry.Status, x.Entry.Title)).Distinct());
        }
    }

    [Fact]
    public void WithinACatalogEveryUrnIsUnique()
    {
        foreach (var (source, entries) in Catalogs)
        {
            var urns = entries.Select(e => e.Urn).ToList();
            Assert.True(urns.Count == urns.Distinct(StringComparer.Ordinal).Count(), $"{source} repeats a URN.");
        }
    }

    private static List<(string Urn, int Status)> ReadContractErrors()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BirraPoint.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var contract = File.ReadAllText(Path.Combine(dir.FullName, "specs", "001-birrapoint-mvp", "contracts", "rest-api.md"));
        var section = contract[contract.IndexOf("## Error catalog", StringComparison.Ordinal)..];

        return ContractRow().Matches(section)
            .Select(m => (UrnPrefix + m.Groups["urn"].Value, int.Parse(m.Groups["status"].Value, CultureInfo.InvariantCulture)))
            .ToList();
    }

    [GeneratedRegex(@"^\| `(?<urn>[a-z-]+)` \| (?<status>\d{3}) \|", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ContractRow();
}
