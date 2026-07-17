var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL 16 (constitution stack; prod runs the same major in ACA, FR-047).
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("16")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);
var db = postgres.AddDatabase("db", "birrapoint");

// Keycloak 26 (constitution: 25+) with the birrapoint realm auto-imported.
// Bootstrap admin + realm seed credentials are LOCAL-DEV placeholders only;
// production injects real secrets at deploy time (FR-046).
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.2")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
    .WithBindMount("../../../infra/keycloak", "/opt/keycloak/data/import", isReadOnly: true)
    .WithHttpEndpoint(port: 8081, targetPort: 8080, name: "http")
    .WithExternalHttpEndpoints();
var keycloakHttp = keycloak.GetEndpoint("http");

// Mailpit SMTP sink (invitations/results land here locally; UI on the http endpoint).
var mailpit = builder.AddMailPit("mailpit");
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
    .WithExternalHttpEndpoints();

// Angular PWA via ng serve (fixed :4200, matching the SPA client redirect URIs).
builder.AddNpmApp("frontend", "../../../frontend", "start")
    .WithHttpEndpoint(port: 4200, isProxied: false)
    .WithExternalHttpEndpoints()
    .WaitFor(api);

builder.Build().Run();
