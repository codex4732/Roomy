using Microsoft.EntityFrameworkCore;
using Roomy.Api.Common.Persistence;
using Roomy.Api.Tenants;

namespace Roomy.Api.Common.Tenancy;

/// <summary>
/// Resolves the tenant for the current request and binds it to <see cref="ITenantContext"/>
/// (technical design §3). Cloud resolves by subdomain, self-hosted by path prefix or a
/// single-tenant default; platform routes run without a tenant.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    private const string PlatformPrefix = "/api/v1/platform";

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, RoomyDbContext db)
    {
        if (ShouldResolveTenant(context.Request.Path))
        {
            var slug = ExtractSlug(context);

            if (slug is null || !TenantSlug.IsValid(slug))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // TODO(auth): once JWT auth lands, verify the token's tenant_id claim matches
            // the resolved tenant (technical design §3 step 2).
            var tenantId = await db.Tenants
                .Where(t => t.Slug == slug && t.Status == TenantStatus.Active)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(context.RequestAborted);

            if (tenantId is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            tenantContext.Bind(tenantId.Value);
        }

        await next(context);
    }

    private static bool ShouldResolveTenant(PathString path) =>
        path.StartsWithSegments("/api")
        && !path.StartsWithSegments(PlatformPrefix);

    private static string? ExtractSlug(HttpContext context)
    {
        // Subdomain (cloud): acme.roomy.app -> "acme".
        var host = context.Request.Host.Host;
        var labels = host.Split('.');
        if (labels.Length >= 3)
        {
            return labels[0];
        }

        // Header fallback for local development and path-mode self-hosting until the
        // reverse-proxy story is wired up.
        return context.Request.Headers["X-Roomy-Tenant"].FirstOrDefault();
    }
}
