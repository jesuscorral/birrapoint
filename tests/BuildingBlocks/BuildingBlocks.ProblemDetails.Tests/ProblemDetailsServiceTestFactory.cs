using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.ProblemDetails.Tests;

/// <summary>Builds the real <see cref="IProblemDetailsService"/> DI would provide, so handler
/// tests exercise the same content-type/serialization behavior as production.</summary>
internal static class ProblemDetailsServiceTestFactory
{
    public static IProblemDetailsService Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        return services.BuildServiceProvider().GetRequiredService<IProblemDetailsService>();
    }
}
