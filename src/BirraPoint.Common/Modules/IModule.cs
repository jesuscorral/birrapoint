using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Common.Modules;

/// <summary>What the host needs from a module: its services and its endpoints. The host holds the explicit list of modules.</summary>
public interface IModule
{
    void AddServices(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
