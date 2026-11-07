using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Keycloak.Authentication.Tests;

public sealed class KeycloakJwtBearerTests
{
    private static ServiceProvider Build(Action<KeycloakJwtBearerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeycloakJwtBearer(options =>
        {
            options.Authority = "https://idp.test/realms/r";
            options.Audience = "the-api";
            options.TokenQueryPaths.Add("/hubs/live");
            configure?.Invoke(options);
        });
        return services.BuildServiceProvider();
    }

    private static JwtBearerOptions JwtOptions(ServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

    private static async Task<string?> TokenForAsync(ServiceProvider provider, string path, string? accessToken)
    {
        var options = JwtOptions(provider);
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        if (accessToken is not null)
        {
            http.Request.QueryString = new QueryString($"?access_token={accessToken}");
        }

        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var context = new MessageReceivedContext(http, scheme, options);
        await options.Events.MessageReceived(context);
        return context.Token;
    }

    [Fact]
    public async Task RegistersTheJwtBearerSchemeAsDefault()
    {
        await using var provider = Build();
        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var scheme = await schemes.GetDefaultAuthenticateSchemeAsync();

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, scheme?.Name);
    }

    [Fact]
    public async Task AppliesAuthorityAndPinsTheAudience()
    {
        await using var provider = Build();

        var options = JwtOptions(provider);

        Assert.Equal("https://idp.test/realms/r", options.Authority);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.Equal("the-api", options.TokenValidationParameters.ValidAudience);
    }

    [Fact]
    public async Task KeepsKeycloakRawClaimNames()
    {
        await using var provider = Build();

        Assert.False(JwtOptions(provider).MapInboundClaims);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HttpsMetadataFollowsTheOption(bool requireHttps)
    {
        await using var provider = Build(options => options.RequireHttpsMetadata = requireHttps);

        Assert.Equal(requireHttps, JwtOptions(provider).RequireHttpsMetadata);
    }

    [Fact]
    public async Task RequiresHttpsMetadataByDefault()
    {
        await using var provider = Build();

        Assert.True(JwtOptions(provider).RequireHttpsMetadata);
    }

    [Theory]
    [InlineData("/hubs/live")]
    [InlineData("/HUBS/live/negotiate")]
    public async Task AcceptsTheQueryTokenOnConfiguredPaths(string path)
    {
        await using var provider = Build();

        Assert.Equal("tok", await TokenForAsync(provider, path, "tok"));
    }

    [Theory]
    [InlineData("/api/competitions")]
    [InlineData("/hubs/liveness")]
    [InlineData("/")]
    public async Task IgnoresTheQueryTokenOnAnyOtherPath(string path)
    {
        await using var provider = Build();

        Assert.Null(await TokenForAsync(provider, path, "tok"));
    }

    [Fact]
    public async Task LeavesTheTokenUnsetWhenThereIsNoQueryToken()
    {
        await using var provider = Build();

        Assert.Null(await TokenForAsync(provider, "/hubs/live", accessToken: null));
    }

    [Fact]
    public async Task NoConfiguredPathsMeansTheQueryTokenIsNeverAccepted()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeycloakJwtBearer(options =>
        {
            options.Authority = "https://idp.test/realms/r";
            options.Audience = "the-api";
        });
        await using var provider = services.BuildServiceProvider();

        Assert.Null(await TokenForAsync(provider, "/hubs/live", "tok"));
    }

    [Fact]
    public async Task RegistersTheRealmRolesClaimsTransformation()
    {
        await using var provider = Build();
        var transformation = provider.GetRequiredService<IClaimsTransformation>();
        var identity = new ClaimsIdentity([new Claim("realm_access", """{"roles":["A"]}""")], "test");

        var principal = await transformation.TransformAsync(new ClaimsPrincipal(identity));

        Assert.True(principal.IsInRole("A"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankAuthorityFailsFastNamingTheSetting(string? authority)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(options => options.Authority = authority));

        Assert.Contains("Authority", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankAudienceFailsFastNamingTheSetting(string? audience)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(options => options.Audience = audience));

        Assert.Contains("Audience", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullConfigureDelegateIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddKeycloakJwtBearer(null!));
    }
}
