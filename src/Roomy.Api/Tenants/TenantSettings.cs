namespace Roomy.Api.Tenants;

/// <summary>Tenant-level booking policies (FR-5.3), stored as JSON on the tenant row.</summary>
public sealed class TenantSettings
{
    public int BookingWindowDays { get; set; } = 60;
    public int MaxDurationMinutes { get; set; } = 480;
    public int MinDurationMinutes { get; set; } = 15;
    public int MaxActiveBookingsPerUser { get; set; } = 10;
    public int CheckinGraceMinutes { get; set; } = 10;
    public int ApprovalExpiryHours { get; set; } = 48;
}
