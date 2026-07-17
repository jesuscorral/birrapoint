using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BirraPoint.Api.Common.OpenApi;

public static class OpenApiExtensions
{
    /// <summary>
    /// Registers OpenAPI document generation with a Bearer JWT security scheme, so Swagger UI's
    /// "Authorize" button can attach a Keycloak-issued access token to try-it-out calls.
    /// </summary>
    public static IServiceCollection AddBirraPointOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });
        return services;
    }

    /// <summary>
    /// Serves the OpenAPI document at <c>/openapi/v1.json</c> (per contracts/rest-api.md) and a
    /// Swagger UI at <c>/swagger</c> to call endpoints interactively. Development-only, matching
    /// the /health and /alive gating in ServiceDefaults.
    /// </summary>
    public static WebApplication MapBirraPointOpenApi(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        // .AllowAnonymous() because the auth fallback policy (T011) denies-by-default on every
        // matched endpoint; these are dev-only diagnostic routes, not part of the protected API.
        app.MapOpenApi().AllowAnonymous();
        app.MapSwaggerUI("swagger", options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "BirraPoint API v1");
        }).AllowAnonymous();

        return app;
    }

    private sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider)
        : IOpenApiDocumentTransformer
    {
        public async Task TransformAsync(
            OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            var schemes = await authenticationSchemeProvider.GetAllSchemesAsync();
            if (!schemes.Any(scheme => scheme.Name == JwtBearerDefaults.AuthenticationScheme))
            {
                return;
            }

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Keycloak-issued JWT access token.",
            };

            var bearerReference = new OpenApiSecuritySchemeReference("Bearer", document);
            foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations!.Values))
            {
                operation.Security ??= new List<OpenApiSecurityRequirement>();
                operation.Security.Add(new OpenApiSecurityRequirement { [bearerReference] = [] });
            }
        }
    }
}
