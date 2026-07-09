using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery, resilience (FR-048).
builder.AddServiceDefaults();

// PostgreSQL via the "db" connection string the AppHost injects (quickstart env contract).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("db")));

// JWT bearer against Keycloak, deny-by-default fallback policy, ORGANIZER/JUDGE role policies (T011).
builder.Services.AddKeycloakAuthentication(builder.Configuration, builder.Environment.IsDevelopment());

// ProblemDetails + exception-handler chain for the 14 urn:birrapoint:* error types (T012).
builder.Services.AddProblemDetailsErrorHandling();

var app = builder.Build();

// Must run first so it wraps every downstream middleware/endpoint.
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

// /health and /alive endpoints (Development only by default).
app.MapDefaultEndpoints();

// EF migrations apply on startup in Development only (T009); production migrates at deploy time.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.Run();
