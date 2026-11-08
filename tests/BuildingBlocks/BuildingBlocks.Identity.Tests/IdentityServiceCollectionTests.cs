using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Identity.Tests;

public sealed class IdentityServiceCollectionTests
{
    [Fact]
    public void RegistersTheHttpContextAccessorAndAScopedCurrentUser()
    {
        var services = new ServiceCollection();
        services.AddCurrentUser();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.NotNull(provider.GetRequiredService<IHttpContextAccessor>());
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var a = first.ServiceProvider.GetRequiredService<ICurrentUser>();
        Assert.Same(a, first.ServiceProvider.GetRequiredService<ICurrentUser>());
        Assert.NotSame(a, second.ServiceProvider.GetRequiredService<ICurrentUser>());
    }
}
