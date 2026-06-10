using Microsoft.EntityFrameworkCore;
using Roomy.Api.Common.Persistence;
using Roomy.Api.Common.Tenancy;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddDbContext<RoomyDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Roomy")));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<RoomyDbContext>("database");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<TenantResolutionMiddleware>();

// Liveness: process is up. Readiness: dependencies (DB) reachable. See technical design §12.
app.MapHealthChecks("/healthz", new() { Predicate = _ => false });
app.MapHealthChecks("/readyz");

var api = app.MapGroup("/api/v1");

api.MapGet("/ping", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
