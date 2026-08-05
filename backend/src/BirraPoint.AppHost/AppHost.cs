var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL 16 (constitution stack; prod runs the same major in ACA, FR-047).
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("16")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
var db = postgres.AddDatabase("db", "birrapoint");
// Keycloak's own store (below) — a second logical database on the same Postgres server/volume.
var keycloakDb = postgres.AddDatabase("keycloakdb", "keycloak");
var postgresEndpoint = postgres.GetEndpoint("tcp");

// Keycloak 26 (constitution: 25+) with the birrapoint realm auto-imported.
// Bootstrap admin + realm seed credentials are LOCAL-DEV placeholders only;
// production injects real secrets at deploy time (FR-046).
// Realm import uses the IGNORE_EXISTING strategy, so it only seeds the realm once — runtime state
// (self-registered organizers, judges provisioned via the Admin API) survives container
// recreation via Keycloak's own database instead of vanishing on every `dotnet run`. This used to
// be dev-mode's embedded H2, file-backed via a host bind mount ("infra/keycloak/.data/h2" — a
// named Docker volume was tried first, matching the Postgres pattern above, but a fresh named
// volume is created root-owned and H2 runs as the image's non-root "keycloak" user, which threw
// AccessDeniedException). H2's single-file MVStore turned out to be the bigger problem: it is not
// crash-safe, and an ungraceful stop (Docker Desktop restart, host sleep, a force-killed AppHost)
// silently corrupted it in practice, breaking existing users' stored credentials while the actual
// competition data in Postgres stayed completely intact — found 2026-08-05 debugging exactly that
// ("can't log in any more, did my imported beers/judges disappear?" — they hadn't; only the
// Keycloak-side credential had). Pointing Keycloak at the same Postgres instance we already run
// (WAL + fsync, the same durability the app's own data relies on) removes that whole failure mode
// and the bind-mount permission workaround together.
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.2")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
    .WithEnvironment("KC_DB", "postgres")
    .WithEnvironment("KC_DB_URL",
        ReferenceExpression.Create($"jdbc:postgresql://{postgresEndpoint.Property(EndpointProperty.HostAndPort)}/keycloak"))
    .WithEnvironment("KC_DB_USERNAME", "postgres")
    .WithEnvironment("KC_DB_PASSWORD", ReferenceExpression.Create($"{postgres.Resource.PasswordParameter}"))
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
