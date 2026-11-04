using System.Text.Json.Serialization;
using System.Threading.Channels;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Behaviors;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Common.RateLimiting;
using BirraPoint.Api.Common.Secrets;
using BirraPoint.Api.Modules.Catalog;
using BirraPoint.Api.Modules.Judging;
using BirraPoint.Api.Modules.Organization;
using BirraPoint.Api.Modules.Publication;
using BirraPoint.Api.Realtime;
using BirraPoint.ServiceDefaults;
using BuildingBlocks.Audit;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Community license — required before the first document generation or QuestPDF throws.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Secrets from Key Vault through the Dapr secret store when Dapr:SecretStore is set (Azure,
// ADR-0021); first, so every registration below already sees them. Locally: plain settings.
builder.Configuration.AddDaprSecrets();

// OpenTelemetry, health checks, service discovery, resilience (FR-048).
builder.AddServiceDefaults();

// PostgreSQL via the "db" connection string the AppHost injects (quickstart env contract).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("db")));

// Readiness: /health runs this, /alive does not (Neon scale-to-zero, ADR-0025).
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("db", tags: ["ready"]);

// JWT bearer against Keycloak, deny-by-default fallback policy, ORGANIZER/JUDGE role policies,
// audience validation pinned to Keycloak:ApiAudience via the birrapoint-spa audience mapper (ADR-0009).
builder.Services.AddKeycloakAuthentication(builder.Configuration, builder.Environment.IsDevelopment());

// Per-organizer throttle on POST .../judges/notify (FR-065).
builder.Services.ConfigureRateLimiting();

// ProblemDetails + exception-handler chain for the 15 urn:birrapoint:* error types.
builder.Services.AddProblemDetailsErrorHandling();

// OpenAPI document, served at /openapi/v1.json in Development (arrives with the first
// business endpoint).
builder.Services.AddOpenApi();

// Enum fields in HTTP JSON responses serialize as their name, matching the SignalR wire format
// configured below (ADR-0007).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// MediatR + FluentValidation ValidationBehavior pipeline.
builder.Services.AddMediatRWithValidation(typeof(Program).Assembly);

// Immutable audit trail writer (BuildingBlocks.Audit, ADR-0035); the actor is the Keycloak subject.
builder.Services.AddAudit<AppDbContext, CurrentUserAuditActor>();

// CompetitionHub + emit-after-commit event dispatcher. Enum payload fields (e.g.
// DispatchProgress's `status`) serialize as their name, not the default int, matching the
// string-enum convention already used for the domain in the database (ADR-0004) and the wire
// examples in contracts/rest-api.md.
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<IEventPublisher, EventPublisher>();

// DispatchJob queue + hosted worker (R-06): Channel<Guid> wakes the worker immediately on
// enqueue; the worker's own periodic safety-net poll (Dispatch:SafetyNetPollInterval) covers any missed signal.
builder.Services.AddSingleton(Channel.CreateUnbounded<Guid>());
builder.Services.AddSingleton<IDispatchWakeUp, ChannelDispatchWakeUp>();
builder.Services.AddSingleton<IValidateOptions<DispatchOptions>, DispatchOptionsValidator>();
builder.Services.AddScoped<IDispatchJobQueue, DispatchJobQueue>();
builder.Services.AddOptions<DispatchOptions>()
    .BindConfiguration(DispatchOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddHostedService<DispatchWorker>();

// Judge provisioning (R-10): Keycloak Admin REST API via a typed HttpClient (client-credentials
// grant against Keycloak:AdminClientId/AdminClientSecret).
builder.Services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>();
builder.Services.AddSingleton<IValidateOptions<KeycloakAdminOptions>, KeycloakAdminOptionsValidator>();
builder.Services.AddOptions<KeycloakAdminOptions>()
    .BindConfiguration(KeycloakAdminOptions.SectionName)
    .ValidateOnStart();

// Invitation/result email delivery (R-10): MailKit against Smtp:Host/Smtp:Port (Mailpit locally).
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();

// Module registrations: contract implementations plus the module's DispatchJob handlers
// (auto-discovered by DispatchWorker): Organization (SendInvitation, judge account provisioning,
// R-20/US14) and Publication (results pipeline GeneratePdfs -> BundleZip -> SendResultEmail,
// FR-036/FR-040/FR-041).
builder.Services
    .AddCatalogModule()
    .AddOrganizationModule()
    .AddJudgingModule()
    .AddPublicationModule();

// The Angular dev server runs on a different origin (:4200) than the API (:5121/:7075) — the
// browser needs this to call REST/hub endpoints directly. Development only; production
// topology (same-origin behind ACA ingress or otherwise) is a Phase 16 decision. No
// AllowCredentials: auth is bearer-token (header or SignalR's ?access_token=), never cookies.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy => policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()));
}

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

var app = builder.Build();

// Production topology: the API runs behind an nginx reverse proxy inside the ACA
// environment (internal-only ingress; TLS terminates at ACA ingress, nginx talks to the API over
// plain HTTP forwarding X-Forwarded-For/Proto/Host). Runs first, before any other middleware that
// reads Request.Scheme/Host. KnownNetworks/KnownProxies are cleared because ACA's internal network
// ranges aren't a stable, publishable value — acceptable here because the API itself has
// internal-only ingress, so only same-environment traffic (i.e. the nginx sidecar app) can reach
// it in the first place. No UseHttpsRedirection: that would redirect nginx's internal plain-HTTP
// calls; ACA ingress is what terminates and enforces HTTPS for external clients.
if (!app.Environment.IsDevelopment())
{
    var forwardedHeadersOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
    };
    forwardedHeadersOptions.KnownIPNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeadersOptions);

    app.UseHsts();
}

// Must run first so it wraps every downstream middleware/endpoint.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();

// Enforces the NotifyJudges rate limit (FR-065); all other endpoints are unthrottled.
app.UseRateLimiter();

// /health and /alive endpoints (Development only by default).
app.MapDefaultEndpoints();

// OpenAPI document, gated to Development like the health endpoints above, plus the
// Swagger UI on top of it at /swagger (Swashbuckle UI middleware only — document generation
// stays with the built-in Microsoft.AspNetCore.OpenApi). AllowAnonymous: the fallback policy
// (AuthenticationExtensions.cs) requires an authenticated user on every mapped endpoint by
// default, which would otherwise 401 the doc fetch and break the Swagger UI page.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapSwaggerUI("swagger", options => options.SwaggerEndpoint("/openapi/v1.json", "BirraPoint API v1")).AllowAnonymous();
}

// CompetitionHub: server → client notifications only.
app.MapHub<CompetitionHub>("/hubs/competition");

// Catalog module: read-only BJCP 2021 styles catalog.
app.MapCatalogModule();

// Organization module: competitions wizard/lifecycle, beer-entry and judge-roster
// import, judge registration/invitations.
app.MapOrganizationModule();

// Judging module: tasting tables (COI validation, BOS flagging), judge workspace (blind samples,
// fixed order), evaluations, organizer monitoring dashboard.
app.MapJudgingModule();

// Publication module: archive download, per-participant email status, manual retry.
app.MapPublicationModule();

// EF migrations apply on startup in Development; outside Development, only when
// Database:MigrateOnStartup=true (Terraform sets this on the single long-running API
// replica, so every start of that replica migrates over Neon before serving traffic). The BJCP
// seed ships as part of the migrations, so this covers it too. Migrates over the non-pooled
// ConnectionStrings:dbDirect when configured (Neon's pooled ConnectionStrings:db breaks EF's
// session-scoped migration advisory lock under pgbouncer transaction pooling).
if (StartupMigrations.ShouldMigrate(app.Environment.IsDevelopment(), app.Configuration))
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.SetConnectionString(StartupMigrations.ResolveMigrationConnectionString(app.Configuration));
    await context.Database.MigrateAsync();
}

await app.RunAsync();
