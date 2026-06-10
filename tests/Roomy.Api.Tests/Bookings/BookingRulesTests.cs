using Roomy.Api.Bookings;
using Roomy.Api.Tenants;

namespace Roomy.Api.Tests.Bookings;

public class BookingRulesTests
{
    private static readonly TenantSettings Settings = new();
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 6, 11, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_booking_passes() =>
        Assert.Null(BookingRules.Validate(At(10), At(11), Now, Settings));

    [Theory]
    [InlineData(10, 7, 10, 37)]   // not snapped
    [InlineData(11, 0, 10, 0)]    // end before start
    [InlineData(10, 0, 10, 0)]    // zero length
    public void Invalid_times_are_rejected(int sh, int sm, int eh, int em) =>
        Assert.NotNull(BookingRules.Validate(At(sh, sm), At(eh, em), Now, Settings));

    [Fact]
    public void Booking_longer_than_a_month_is_rejected() =>
        Assert.Contains("days",
            BookingRules.Validate(At(10), At(10).AddDays(31), Now, Settings)!);

    [Fact]
    public void Month_long_booking_is_allowed() =>
        Assert.Null(BookingRules.Validate(At(10), At(10).AddDays(30), Now, Settings));

    [Fact]
    public void Custom_max_duration_is_enforced() =>
        Assert.Contains("hours", BookingRules.Validate(At(10), At(19), Now,
            new TenantSettings { MaxDurationMinutes = 480 })!);

    [Fact]
    public void Booking_beyond_window_is_rejected() =>
        Assert.Contains("days in advance",
            BookingRules.Validate(Now.AddDays(61), Now.AddDays(61).AddHours(1), Now, Settings)!);

    [Fact]
    public void Daily_series_expands_consecutive_days()
    {
        var occurrences = BookingRules.ExpandSeries(
            At(9), At(10), SeriesFrequency.Daily, 3, "Europe/London");
        Assert.Equal(3, occurrences.Count);
        Assert.Equal(At(9).AddDays(2), occurrences[2].Start);
    }

    [Fact]
    public void Weekly_series_keeps_local_wall_clock_across_dst()
    {
        // 2026-10-25: Europe/London leaves BST. 09:00 local is 08:00Z before, 09:00Z after.
        var firstStart = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero); // Mon 09:00 BST
        var occurrences = BookingRules.ExpandSeries(
            firstStart, firstStart.AddHours(1), SeriesFrequency.Weekly, 2, "Europe/London");
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), occurrences[1].Start);
    }
}
