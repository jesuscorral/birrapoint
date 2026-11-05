using BirraPoint.Api.Realtime;

namespace BirraPoint.Api.UnitTests.Realtime;

public sealed class CompetitionGroupsTests
{
    [Fact]
    public void OrganizersFormatsTheFixedGroupName()
    {
        var competitionId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Assert.Equal(
            "competition:11111111-1111-1111-1111-111111111111:organizers",
            CompetitionGroups.Organizers(competitionId));
    }

    [Fact]
    public void TableFormatsTheFixedGroupName()
    {
        var tableId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        Assert.Equal("table:22222222-2222-2222-2222-222222222222", CompetitionGroups.Table(tableId));
    }
}
