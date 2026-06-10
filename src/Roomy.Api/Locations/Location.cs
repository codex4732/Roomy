using Roomy.Api.Common.Persistence;

namespace Roomy.Api.Locations;

public sealed class Location : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public required string Name { get; set; }

    /// <summary>IANA time zone identifier; all room times are interpreted in this zone (FR-2.1).</summary>
    public required string Timezone { get; set; }

    public string? Address { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
