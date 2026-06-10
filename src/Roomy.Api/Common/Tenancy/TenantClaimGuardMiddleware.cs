using Roomy.Api.Identity;

namespace Roomy.Api.Common.Tenancy;

/// <summary>
/// Technical design §3 step 2: an authenticated user's token must carry the same tenant
/// the request resolved to. A valid token from tenant A must never act inside tenant B.
/// Runs after authentication, before authorization.
/// </summary>
public sealed class TenantClaimGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated == true && tenantContext.HasTenant)
        {
            var claim = context.User.FindFirst(TokenService.TenantClaim)?.Value;
            if (!Guid.TryParse(claim, out var tokenTenant) || tokenTenant != tenantContext.TenantId)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await next(context);
    }
}
