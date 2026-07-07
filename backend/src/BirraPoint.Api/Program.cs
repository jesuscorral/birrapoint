var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery, resilience (FR-048).
builder.AddServiceDefaults();

var app = builder.Build();

// /health and /alive endpoints (Development only by default).
app.MapDefaultEndpoints();

app.Run();
