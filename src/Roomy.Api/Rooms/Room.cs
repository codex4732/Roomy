using Roomy.Api.Common.Persistence;
using Roomy.Api.Locations;

namespace Roomy.Api.Rooms;

public sealed class Room : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid LocationId { get; set; }

    public Location? Location { get; set; }

    public required string Name { get; set; }

    public int Capacity { get; set; }

    public string? Floor { get; set; }

    public RoomStatus Status { get; set; } = RoomStatus.Active;

    public bool RequiresApproval { get; set; }

    public bool CheckinRequired { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum RoomStatus
{
    Active = 0,
    TemporarilyUnavailable = 1,
    Retired = 2,
}
