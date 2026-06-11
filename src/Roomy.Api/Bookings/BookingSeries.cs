using Roomy.Api.Common.Persistence;

namespace Roomy.Api.Bookings;

/// <summary>A recurring series (FR-4.7); occurrences are materialized `bookings` rows.</summary>
public sealed class BookingSeries : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid RoomId { get; set; }
    public Guid OrganizerId { get; set; }
    public required string Title { get; set; }
    public SeriesFrequency Frequency { get; set; }
    public int Count { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum SeriesFrequency
{
    Daily = 0,
    Weekly = 1,
}
