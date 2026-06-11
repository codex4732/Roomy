using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using Roomy.Api.Blackouts;
using Roomy.Api.Bookings;
using Roomy.Api.Common.Persistence;
using Roomy.Api.Common.Tenancy;
using Roomy.Api.Identity;
using Roomy.Api.Locations;
using Roomy.Api.Tenants;
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
builder.Services.AddScoped<TenantConnectionInterceptor>();

builder.Services.AddDbContext<RoomyDbContext>((sp, options) => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Roomy"))
    .UseSnakeCaseNamingConvention()
    .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>()));

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32,
        "Jwt:SigningKey must be configured with at least 32 bytes.")
    .ValidateOnStart();

builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ClockSkew = TimeSpan.FromSeconds(30),
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Staff", p => p.RequireRole(
        nameof(Roomy.Api.Identity.UserRole.TenantAdmin), nameof(Roomy.Api.Identity.UserRole.FacilityManager)));
    options.AddPolicy("TenantAdmin", p => p.RequireRole(nameof(Roomy.Api.Identity.UserRole.TenantAdmin)));
});

builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("booking-lifecycle");
    q.AddJob<BookingLifecycleJob>(o => o.WithIdentity(jobKey));
    q.AddTrigger(t => t.ForJob(jobKey).WithSimpleSchedule(s => s.WithIntervalInSeconds(60).RepeatForever()).StartNow());
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = false);

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
app.UseAuthentication();
app.UseMiddleware<TenantClaimGuardMiddleware>();
app.UseAuthorization();

// Liveness: process is up. Readiness: dependencies (DB) reachable. See technical design §12.
app.MapHealthChecks("/healthz", new() { Predicate = _ => false });
app.MapHealthChecks("/readyz");

app.MapGroup("/api/v1")
    .MapAuthEndpoints()
    .MapPlatformEndpoints()
    .MapLocationEndpoints()
    .MapBookingEndpoints()
    .MapUserEndpoints()
    .MapBlackoutEndpoints()
    .MapGet("/ping", () => Results.Ok(new { status = "ok" }));

if (app.Environment.IsDevelopment())
{
    await DevSeeder.MigrateAndSeedAsync(app.Services);
}

app.Run();

public partial class Program;
