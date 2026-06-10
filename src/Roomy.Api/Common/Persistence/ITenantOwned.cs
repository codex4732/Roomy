namespace Roomy.Api.Common.Persistence;

/// <summary>
/// Marks an entity as belonging to a tenant. Tenant-owned entities get their
/// <see cref="TenantId"/> stamped automatically on insert, a global query filter,
/// and a row-level-security policy in their migration (technical design §4).
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; set; }
}
