using BirraPoint.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health checks, service discovery, resilience (FR-048).
builder.AddServiceDefaults();

// PostgreSQL via the "db" connection string the AppHost injects (quickstart env contract).
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("db")));

var app = builder.Build();

// /health and /alive endpoints (Development only by default).
app.MapDefaultEndpoints();

// EF migrations apply on startup in Development only (T009); production migrates at deploy time.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.Run();
