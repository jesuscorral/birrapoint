using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BirraPoint.Api.Common.Auth;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("ICurrentUser was accessed outside an HTTP request.");

    public string Sub =>
        Principal.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("Authenticated principal has no 'sub' claim.");

    public string? Email => Principal.FindFirst("email")?.Value;

    public IReadOnlyList<string> Roles =>
        [.. Principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value)];
}
