namespace Roomy.Api.Tenants;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Slug { get; set; }

    public required string Name { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    public TenantSettings Settings { get; set; } = new();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum TenantStatus
{
    Active = 0,
    Suspended = 1,
}
