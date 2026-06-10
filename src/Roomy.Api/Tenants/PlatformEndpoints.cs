using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Roomy.Api.Common.Persistence;
using Roomy.Api.Common.Tenancy;
using Roomy.Api.Identity;

namespace Roomy.Api.Tenants;

/// <summary>
/// Platform Operator endpoints (PRD §3 role 1, FR-1.1). Run without a tenant context.
/// Interim auth: a static API key in `Platform:ApiKey` sent as `X-Platform-Key`;
/// replaced by operator accounts when the operator console lands.
/// </summary>
public static class PlatformEndpoints
{
    public static RouteGroupBuilder MapPlatformEndpoints(this RouteGroupBuilder group)
    {
        var platform = group.MapGroup("/platform");
        platform.AddEndpointFilter(RequirePlatformKeyAsync);

        platform.MapGet("/tenants", ListTenantsAsync);
        platform.MapPost("/tenants", CreateTenantAsync);
        platform.MapPost("/tenants/{id:guid}/suspend", (Guid id, RoomyDbContext db)
            => SetStatusAsync(id, TenantStatus.Suspended, db));
        platform.MapPost("/tenants/{id:guid}/reactivate", (Guid id, RoomyDbContext db)
            => SetStatusAsync(id, TenantStatus.Active, db));

        return group;
    }

    public sealed record CreateTenantRequest(
        string Name, string Slug, string AdminEmail, string AdminName, string AdminPassword);

    public sealed record TenantDto(Guid Id, string Slug, string Name, string Status, DateTimeOffset CreatedAt);

    private static async ValueTask<object?> RequirePlatformKeyAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = context.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["Platform:ApiKey"];

        if (string.IsNullOrEmpty(configured))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                detail: "Platform API is disabled: Platform:ApiKey is not configured.");
        }

        var presented = context.HttpContext.Request.Headers["X-Platform-Key"].FirstOrDefault();
        if (presented != configured)
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }

    private static async Task<IResult> ListTenantsAsync(RoomyDbContext db)
        => Results.Ok(await db.Tenants
            .OrderBy(t => t.Slug)
            .Select(t => new TenantDto(t.Id, t.Slug, t.Name, t.Status.ToString(), t.CreatedAt))
            .ToListAsync());

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest request,
        RoomyDbContext db,
        ITenantContext tenantContext,
        IPasswordHasher<User> passwordHasher)
    {
        if (!TenantSlug.IsValid(request.Slug))
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: $"Slug must be {TenantSlug.MinLength}-{TenantSlug.MaxLength} lowercase letters, digits, or interior hyphens.");
        }

        if (string.IsNullOrWhiteSpace(request.AdminEmail) || string.IsNullOrWhiteSpace(request.AdminName)
            || request.AdminPassword.Length < 12)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: "Admin email, name, and a password of at least 12 characters are required.");
        }

        if (await db.Tenants.AnyAsync(t => t.Slug == request.Slug))
        {
            return Results.Conflict(new { detail = $"Slug '{request.Slug}' is already taken." });
        }

        var tenant = new Tenant { Slug = request.Slug, Name = request.Name };
        db.Tenants.Add(tenant);

        tenantContext.Bind(tenant.Id);
        var admin = new User
        {
            TenantId = tenant.Id,
            Email = request.AdminEmail.ToLowerInvariant(),
            Name = request.AdminName,
            Role = UserRole.TenantAdmin,
        };
        admin.PasswordHash = passwordHasher.HashPassword(admin, request.AdminPassword);
        db.Users.Add(admin);

        await db.SaveChangesAsync();

        return Results.Created(
            $"/api/v1/platform/tenants/{tenant.Id}",
            new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.Status.ToString(), tenant.CreatedAt));
    }

    private static async Task<IResult> SetStatusAsync(Guid id, TenantStatus status, RoomyDbContext db)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null)
        {
            return Results.NotFound();
        }

        tenant.Status = status;
        await db.SaveChangesAsync();
        return Results.Ok(new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.Status.ToString(), tenant.CreatedAt));
    }
}
