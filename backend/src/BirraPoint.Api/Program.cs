using System.Text.Json.Serialization;
using System.Threading.Channels;
using BirraPoint.Api.Common.Audit;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Behaviors;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Realtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

// MediatR + FluentValidation ValidationBehavior pipeline (T013).
builder.Services.AddMediatRWithValidation(typeof(Program).Assembly);

// Immutable audit trail writer (T014).
builder.Services.AddScoped<IAuditWriter, AuditWriter>();

// CompetitionHub + emit-after-commit event dispatcher (T015). Enum payload fields (e.g.
// DispatchProgress's `status`) serialize as their name, not the default int, matching the
// string-enum convention already used for the domain in the database (ADR-0004) and the wire
// examples in contracts/rest-api.md; T017's first REST slice should configure the equivalent for
// HTTP responses so both transports agree on the same wire format.
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<IEventPublisher, EventPublisher>();

// DispatchJob queue + hosted worker (T016/R-06): Channel<Guid> wakes the worker immediately on
// enqueue; the worker's own periodic safety-net poll covers any missed signal.
builder.Services.AddSingleton(Channel.CreateUnbounded<Guid>());
builder.Services.AddScoped<IDispatchJobQueue, DispatchJobQueue>();
builder.Services.AddHostedService<DispatchWorker>();

var app = builder.Build();

// Tracked gap (senior review, PR #6): no audience mapper exists yet on the Keycloak realm's API
// resource, so JWT audience validation is disabled — must close before the first protected
// endpoint (T017). Reads the actual configured value rather than assuming it, so this warning
// self-corrects the moment the gap is closed.
var jwtOptions = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
    .Get(JwtBearerDefaults.AuthenticationScheme);
if (!jwtOptions.TokenValidationParameters.ValidateAudience)
{
    app.Logger.LogWarning(
        "JWT bearer audience validation is disabled (ValidateAudience=false) — see " +
        "Common/Auth/AuthenticationExtensions.cs. Must be closed before any endpoint is protected in production.");
}

// Must run first so it wraps every downstream middleware/endpoint.
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

// /health and /alive endpoints (Development only by default).
app.MapDefaultEndpoints();

// CompetitionHub: server → client notifications only (T015).
app.MapHub<CompetitionHub>("/hubs/competition");

// EF migrations apply on startup in Development only (T009); production migrates at deploy time.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.Run();
