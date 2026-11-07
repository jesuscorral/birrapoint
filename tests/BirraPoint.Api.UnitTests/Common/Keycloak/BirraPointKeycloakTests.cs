using BirraPoint.Api.Common.Keycloak;
using BuildingBlocks.Keycloak.Admin;

namespace BirraPoint.Api.UnitTests.Common.Keycloak;

public sealed class BirraPointKeycloakTests
{
    [Fact]
    public void JudgeAccountsAreGrantedTheJudgeRealmRole()
    {
        Assert.Equal("JUDGE", BirraPointKeycloak.JudgeRole);
    }

    [Fact]
    public void LocalDevPlaceholderMatchesTheSecretTheAppHostAndDevRealmShip()
    {
        Assert.Equal("dev-only-secret-change-me", BirraPointKeycloak.LocalDevPlaceholder);
    }

    [Fact]
    public void ConfigureAdminRegistersThePlaceholderForTheBlockToReject()
    {
        var options = new KeycloakAdminOptions();

        BirraPointKeycloak.ConfigureAdmin(options);

        Assert.Equal(BirraPointKeycloak.LocalDevPlaceholder, options.DevelopmentPlaceholderSecret);
        Assert.Null(options.AdminClientSecret);
    }
}
