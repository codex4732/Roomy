using Roomy.Api.Common.Persistence;

namespace Roomy.Api.Blackouts;

/// <summary>FR-5.4: blocks a room (or a whole location when RoomId is null) for a time range.</summary>
public sealed class BlackoutPeriod : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid LocationId { get; set; }
    public Guid? RoomId { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
