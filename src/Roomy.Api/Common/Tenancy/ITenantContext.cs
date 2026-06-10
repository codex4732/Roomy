namespace Roomy.Api.Common.Tenancy;

/// <summary>
/// The tenant bound to the current request. Populated by <see cref="TenantResolutionMiddleware"/>
/// before any data access happens; consumed by EF Core query filters and the RLS GUC
/// (technical design §3–4).
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }

    bool HasTenant { get; }

    void Bind(Guid tenantId);
}
