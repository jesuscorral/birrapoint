using System.Text.Json.Serialization;
using System.Threading.Channels;
using BirraPoint.Api.Common.Audit;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Behaviors;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Features.Catalog;
using BirraPoint.Api.Features.Competitions;
using BirraPoint.Api.Features.Dispatch;
using BirraPoint.Api.Features.Evaluations;
using BirraPoint.Api.Features.Import;
using BirraPoint.Api.Features.Judges;
using BirraPoint.Api.Features.Monitoring;
using BirraPoint.Api.Features.Tables;
using BirraPoint.Api.Features.TastingOrder;
using BirraPoint.Api.Realtime;
using BirraPoint.ServiceDefaults;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

// Community license (T074) — required before the first document generation or QuestPDF throws.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery, resilience (FR-048).
builder.AddServiceDefaults();

// PostgreSQL via the "db" connection string the AppHost injects (quickstart env contract).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("db")));

// JWT bearer against Keycloak, deny-by-default fallback policy, ORGANIZER/JUDGE role policies (T011),
// audience validation pinned to Keycloak:ApiAudience via the birrapoint-spa audience mapper (T017/ADR-0009).
builder.Services.AddKeycloakAuthentication(builder.Configuration, builder.Environment.IsDevelopment());

// ProblemDetails + exception-handler chain for the 14 urn:birrapoint:* error types (T012).
builder.Services.AddProblemDetailsErrorHandling();

// OpenAPI document, served at /openapi/v1.json in Development (T017 — arrives with the first
// business endpoint).
builder.Services.AddOpenApi();

// Enum fields in HTTP JSON responses serialize as their name, matching the SignalR wire format
// configured below (ADR-0007).
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// MediatR + FluentValidation ValidationBehavior pipeline (T013).
builder.Services.AddMediatRWithValidation(typeof(Program).Assembly);

// Immutable audit trail writer (T014).
builder.Services.AddScoped<IAuditWriter, AuditWriter>();

// CompetitionHub + emit-after-commit event dispatcher (T015). Enum payload fields (e.g.
// DispatchProgress's `status`) serialize as their name, not the default int, matching the
// string-enum convention already used for the domain in the database (ADR-0004) and the wire
// examples in contracts/rest-api.md.
builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<IEventPublisher, EventPublisher>();

// DispatchJob queue + hosted worker (T016/R-06): Channel<Guid> wakes the worker immediately on
// enqueue; the worker's own periodic safety-net poll covers any missed signal.
builder.Services.AddSingleton(Channel.CreateUnbounded<Guid>());
builder.Services.AddScoped<IDispatchJobQueue, DispatchJobQueue>();
builder.Services.AddHostedService<DispatchWorker>();

// Judge provisioning (R-10/T040): Keycloak Admin REST API via a typed HttpClient (client-credentials
// grant against Keycloak:AdminClientId/AdminClientSecret).
builder.Services.AddHttpClient<IKeycloakAdminClient, KeycloakAdminClient>();

// Invitation/result email delivery (R-10/T041): MailKit against Smtp:Host/Smtp:Port (Mailpit locally).
builder.Services.AddScoped<IEmailSender, MailKitEmailSender>();

// First DispatchJobHandler (T041): SendInvitation, auto-discovered by DispatchWorker.
builder.Services.AddScoped<IDispatchJobHandler, SendInvitationHandler>();

// Judge account provisioning without email (T116/R-20/US14) — enqueued eagerly by both
// RegisterJudges and ConsolidateJudgeImport; SendInvitation is enqueued only by the explicit
// "Notify judges" action.
builder.Services.AddScoped<IDispatchJobHandler, ProvisionJudgeAccountHandler>();

// Results dispatch pipeline (T075/FR-036/FR-040/FR-041): GeneratePdfs -> BundleZip -> SendResultEmail,
// each enqueuing the next on success, auto-discovered by DispatchWorker like SendInvitation above.
builder.Services.AddScoped<IDispatchJobHandler, GeneratePdfsHandler>();
builder.Services.AddScoped<IDispatchJobHandler, BundleZipHandler>();
builder.Services.AddScoped<IDispatchJobHandler, SendResultEmailHandler>();

// The Angular dev server runs on a different origin (:4200) than the API (:5121/:7075) — the
// browser needs this to call REST/hub endpoints directly (T020). Development only; production
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

// Production topology (T095-T097): the API runs behind an nginx reverse proxy inside the ACA
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

// /health and /alive endpoints (Development only by default).
app.MapDefaultEndpoints();

// OpenAPI document (T017), gated to Development like the health endpoints above, plus the
// Swagger UI on top of it at /swagger (Swashbuckle UI middleware only — document generation
// stays with the built-in Microsoft.AspNetCore.OpenApi). AllowAnonymous: the fallback policy
// (AuthenticationExtensions.cs) requires an authenticated user on every mapped endpoint by
// default, which would otherwise 401 the doc fetch and break the Swagger UI page.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapSwaggerUI("swagger", options => options.SwaggerEndpoint("/openapi/v1.json", "BirraPoint API v1")).AllowAnonymous();
}

// CompetitionHub: server → client notifications only (T015).
app.MapHub<CompetitionHub>("/hubs/competition");

// First REST slice (T017): GET /api/v1/styles.
app.MapCatalogEndpoints();

// Competitions wizard/lifecycle (T027/T028).
app.MapCompetitionsEndpoints();

// Entry import: upload, mapping/correction, consolidation (T031/T033-T035).
app.MapImportEndpoints();

// Judge bulk registration, invitations, email correction (T042).
app.MapJudgesEndpoints();

// Judge roster import: upload, mapping/correction, consolidation (T115/US14).
app.MapJudgeImportEndpoints();

// Tasting tables: create/update with transactional COI validation + BOS flagging, list (T047).
app.MapTablesEndpoints();

// Judge workspace: assigned tables, blind samples, fix tasting order (T050-T052).
app.MapTastingOrderEndpoints();

// Judge workspace: submit evaluations, idempotent replay (T055-T058).
app.MapEvaluationsEndpoints();

// Organizer dashboard: table progress snapshot, per-entry audit drill-down (T068-T069).
app.MapMonitoringEndpoints();

// Results archive download, per-participant email status, manual retry (T072-T076).
app.MapDispatchEndpoints();

// EF migrations apply on startup in Development (T009); outside Development, only when
// Database:MigrateOnStartup=true (T095-T097 — Terraform sets this on the single long-running API
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
