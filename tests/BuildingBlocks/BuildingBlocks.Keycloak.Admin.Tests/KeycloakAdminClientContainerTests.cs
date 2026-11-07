namespace BuildingBlocks.Keycloak.Admin.Tests;

/// <summary>
/// The client against a real Keycloak (needs Docker), pinning the Admin REST behaviour the
/// fake-handler tests can only assume: least-privilege service account, role lookup through the
/// user-scoped endpoints, and the temporary-password lifecycle.
/// </summary>
public sealed class KeycloakAdminClientContainerTests(KeycloakFixture keycloak) : IClassFixture<KeycloakFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NewUserIsCreatedWithATemporaryPasswordAndTheRole()
    {
        var email = KeycloakFixture.NewEmail();

        var password = await keycloak.CreateClient().EnsureUserWithTemporaryPasswordAsync(email, KeycloakFixture.GrantableRole, Ct);

        Assert.False(string.IsNullOrWhiteSpace(password));
        var user = await keycloak.FindUserAsync(email);
        Assert.NotNull(user);
        Assert.Equal(email, user["username"]!.GetValue<string>());
        Assert.Contains("UPDATE_PASSWORD", user["requiredActions"]!.AsArray().Select(a => a!.GetValue<string>()));
        var userId = user["id"]!.GetValue<string>();
        Assert.Contains(KeycloakFixture.GrantableRole, await keycloak.RealmRolesOfAsync(userId));
        Assert.Equal("password", (await keycloak.PasswordCredentialAsync(userId))["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task StillPendingAccountGetsANewTemporaryPassword()
    {
        var email = KeycloakFixture.NewEmail();
        var client = keycloak.CreateClient();
        var first = await client.EnsureUserWithTemporaryPasswordAsync(email, KeycloakFixture.GrantableRole, Ct);
        var userId = (await keycloak.FindUserAsync(email))!["id"]!.GetValue<string>();
        var firstCredential = (await keycloak.PasswordCredentialAsync(userId))["createdDate"]!.GetValue<long>();

        var second = await client.EnsureUserWithTemporaryPasswordAsync(email, KeycloakFixture.GrantableRole, Ct);

        Assert.False(string.IsNullOrWhiteSpace(second));
        Assert.NotEqual(first, second);
        Assert.NotEqual(firstCredential, (await keycloak.PasswordCredentialAsync(userId))["createdDate"]!.GetValue<long>());
        Assert.Contains("UPDATE_PASSWORD", (await keycloak.FindUserAsync(email))!["requiredActions"]!.AsArray().Select(a => a!.GetValue<string>()));
    }

    [Fact]
    public async Task AccountThatCompletedSetupKeepsItsPasswordAndReturnsNull()
    {
        var email = KeycloakFixture.NewEmail();
        var client = keycloak.CreateClient();
        await client.EnsureUserWithTemporaryPasswordAsync(email, KeycloakFixture.GrantableRole, Ct);
        var user = (await keycloak.FindUserAsync(email))!;
        var userId = user["id"]!.GetValue<string>();
        await keycloak.ClearRequiredActionsAsync(user);
        var credentialBefore = (await keycloak.PasswordCredentialAsync(userId))["createdDate"]!.GetValue<long>();

        var password = await client.EnsureUserWithTemporaryPasswordAsync(email, KeycloakFixture.GrantableRole, Ct);

        Assert.Null(password);
        Assert.Equal(credentialBefore, (await keycloak.PasswordCredentialAsync(userId))["createdDate"]!.GetValue<long>());
        Assert.Empty((await keycloak.FindUserAsync(email))!["requiredActions"]!.AsArray());
    }

    [Fact]
    public async Task GrantingTheRoleTwiceIsIdempotent()
    {
        var email = KeycloakFixture.NewEmail();
        var client = keycloak.CreateClient();
        await client.EnsureUserWithTemporaryPasswordAsync(email, KeycloakFixture.GrantableRole, Ct);
        var userId = (await keycloak.FindUserAsync(email))!["id"]!.GetValue<string>();

        await client.EnsureUserWithTemporaryPasswordAsync(email, KeycloakFixture.GrantableRole, Ct);

        Assert.Single(await keycloak.RealmRolesOfAsync(userId), role => role == KeycloakFixture.GrantableRole);
    }

    [Fact]
    public async Task UnknownRoleThrowsNamingTheRole()
    {
        var email = KeycloakFixture.NewEmail();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => keycloak.CreateClient().EnsureUserWithTemporaryPasswordAsync(email, "NO_SUCH_ROLE", Ct));

        Assert.Contains("NO_SUCH_ROLE", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateUserEmailChangesEmailAndUsername()
    {
        var oldEmail = KeycloakFixture.NewEmail();
        var newEmail = KeycloakFixture.NewEmail();
        var client = keycloak.CreateClient();
        await client.EnsureUserWithTemporaryPasswordAsync(oldEmail, KeycloakFixture.GrantableRole, Ct);

        await client.UpdateUserEmailAsync(oldEmail, newEmail, Ct);

        Assert.Null(await keycloak.FindUserAsync(oldEmail));
        var user = await keycloak.FindUserAsync(newEmail);
        Assert.NotNull(user);
        Assert.Equal(newEmail, user["username"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateUserEmailForAnUnknownAddressIsANoOp()
    {
        var oldEmail = KeycloakFixture.NewEmail();
        var newEmail = KeycloakFixture.NewEmail();

        await keycloak.CreateClient().UpdateUserEmailAsync(oldEmail, newEmail, Ct);

        Assert.Null(await keycloak.FindUserAsync(newEmail));
    }

    [Fact]
    public async Task WrongClientSecretIsRejected()
    {
        var client = keycloak.CreateClient(secret: "not-the-secret");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.EnsureUserWithTemporaryPasswordAsync(KeycloakFixture.NewEmail(), KeycloakFixture.GrantableRole, Ct));
    }
}
