namespace Roomy.Api.Common.Tenancy;

public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public Guid TenantId => _tenantId
        ?? throw new InvalidOperationException("No tenant is bound to the current request.");

    public bool HasTenant => _tenantId.HasValue;

    public void Bind(Guid tenantId)
    {
        if (_tenantId.HasValue)
        {
            throw new InvalidOperationException("A tenant is already bound to the current request.");
        }

        _tenantId = tenantId;
    }
}
