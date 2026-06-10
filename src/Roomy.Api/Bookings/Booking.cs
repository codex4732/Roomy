using Roomy.Api.Common.Persistence;
using Roomy.Api.Identity;
using Roomy.Api.Rooms;

namespace Roomy.Api.Bookings;

public sealed class Booking : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid RoomId { get; set; }

    public Room? Room { get; set; }

    public Guid OrganizerId { get; set; }

    public User? Organizer { get; set; }

    public required string Title { get; set; }

    public DateTimeOffset StartAt { get; set; }

    public DateTimeOffset EndAt { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;

    public int AttendeeCount { get; set; } = 1;

    public Guid? SeriesId { get; set; }

    public DateTimeOffset? CheckedInAt { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// FR-4.2 / technical design §6.3. Only states 0–2 block a room's slot — the
/// `bookings_no_overlap` exclusion constraint's WHERE clause depends on these
/// numeric values, so renumbering requires a migration.
/// </summary>
public enum BookingStatus
{
    PendingApproval = 0,
    Confirmed = 1,
    CheckedIn = 2,
    Completed = 3,
    Declined = 4,
    Cancelled = 5,
    AutoReleased = 6,
}
