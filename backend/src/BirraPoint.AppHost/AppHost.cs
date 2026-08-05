var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL 16 (constitution stack; prod runs the same major in ACA, FR-047).
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("16")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
var db = postgres.AddDatabase("db", "birrapoint");
// Keycloak's own store (below) — a second logical database on the same Postgres server/volume.
// Re-seeding the realm from scratch (birrapoint-realm.json edits, or recovering from the
// corruption incident ADR-0013 describes) means dropping this database, not clearing a folder —
// `docker exec <postgres container> psql -U postgres -c 'DROP DATABASE keycloak'` before the next
// `dotnet run`. Wiping the Postgres data volume now resets Keycloak and the app's own data
// together, not independently (ADR-0013).
var keycloakDb = postgres.AddDatabase("keycloakdb", "keycloak");

// Keycloak 26 (constitution: 25+) with the birrapoint realm auto-imported.
// Bootstrap admin + realm seed credentials are LOCAL-DEV placeholders only;
// production injects real secrets at deploy time (FR-046).
// Realm import uses the IGNORE_EXISTING strategy, so it only seeds the realm once. Backed by
// Postgres (keycloakDb above), not dev-mode's default embedded H2 — H2's single-file store isn't
// crash-safe and was found silently corrupting itself on an ungraceful stop, breaking stored
// user credentials while the app's own Postgres data stayed intact; see ADR-0013 for the full
// incident and why a host bind mount was tried first for H2 (kept only as history there, since
// H2 is gone).
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.2")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_URL", keycloakDb.Resource.JdbcConnectionString)
    .WithEnvironment("KC_DB_USERNAME", postgres.Resource.UserNameReference)
    .WithEnvironment("KC_DB_PASSWORD", postgres.Resource.PasswordParameter)
    .WithBindMount("../../../infra/keycloak", "/opt/keycloak/data/import", isReadOnly: true)
    .WithBindMount("../../../infra/keycloak/themes/birrapoint", "/opt/keycloak/themes/birrapoint", isReadOnly: true)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithHttpEndpoint(port: 8081, targetPort: 8080, name: "http")
    .WithExternalHttpEndpoints()
    .WaitFor(keycloakDb);
var keycloakHttp = keycloak.GetEndpoint("http");

// Mailpit SMTP sink (invitations/results land here locally; UI/REST API on the http endpoint,
// pinned to :8025 like Keycloak's :8081 above — quickstart.md documents it and T044's E2E needs
// a deterministic address to reach Mailpit's REST API).
var mailpit = builder.AddMailPit("mailpit", httpPort: 8025);
var smtp = mailpit.GetEndpoint("smtp");

// Backend API: EF migrations + BJCP seed run on startup in Development (T009/T010).
var api = builder.AddProject<Projects.BirraPoint_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("Keycloak__Authority",
        ReferenceExpression.Create($"{keycloakHttp.Property(EndpointProperty.Url)}/realms/birrapoint"))
    .WithEnvironment("Keycloak__ApiAudience", "birrapoint-api")
    .WithEnvironment("Keycloak__AdminClientId", "birrapoint-api-admin")
    .WithEnvironment("Keycloak__AdminClientSecret", "dev-only-secret-change-me")
    .WithEnvironment("Smtp__Host", ReferenceExpression.Create($"{smtp.Property(EndpointProperty.Host)}"))
    .WithEnvironment("Smtp__Port", ReferenceExpression.Create($"{smtp.Property(EndpointProperty.Port)}"))
    // Fixed dev port (matches the SPA's :4200 below and its Keycloak redirect URIs) — used to
    // build the login link in invitation/result emails (T041).
    .WithEnvironment("Frontend__BaseUrl", "http://localhost:4200")
    .WithExternalHttpEndpoints();

// Angular PWA via ng serve (fixed :4200, matching the SPA client redirect URIs).
builder.AddNpmApp("frontend", "../../../frontend", "start")
    .WithHttpEndpoint(port: 4200, isProxied: false)
    .WithExternalHttpEndpoints()
    .WaitFor(api);

builder.Build().Run();
