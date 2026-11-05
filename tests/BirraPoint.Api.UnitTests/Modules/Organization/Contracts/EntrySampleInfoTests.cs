using BirraPoint.Api.Modules.Organization.Contracts;

namespace BirraPoint.Api.UnitTests.Modules.Organization.Contracts;

/// <summary>BR-01/FR-019 anonymity boundary: the entry shape Judging receives physically lacks entrant
/// fields (beer name, participant, collaborators, brewery, origin, recipe).</summary>
public class EntrySampleInfoTests
{
    [Fact]
    public void ExposesOnlyAnonymousSampleFields()
    {
        var properties = typeof(EntrySampleInfo).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "AbvPercent",
                "BlindCode",
                "CompetitionCategoryName",
                "CompetitionId",
                "EntryInstructions",
                "Id",
                "NotValidForBos",
                "StyleCode",
            ],
            properties);
    }
}
