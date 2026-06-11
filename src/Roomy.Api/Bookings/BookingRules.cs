using NodaTime;
using Roomy.Api.Tenants;

namespace Roomy.Api.Bookings;

/// <summary>Pure booking validation and series expansion, unit-testable without a database.</summary>
public static class BookingRules
{
    public const int SnapMinutes = 15;
    public const int MaxSeriesOccurrences = 180;

    /// <summary>Returns a problem code (FR-5.5) or null when valid.</summary>
    public static string? Validate(DateTimeOffset start, DateTimeOffset end, DateTimeOffset now, TenantSettings settings)
    {
        if (!IsSnapped(start) || !IsSnapped(end))
        {
            return $"Times must align to {SnapMinutes}-minute increments.";
        }
        if (end <= start)
        {
            return "End time must be after the start time.";
        }
        var duration = end - start;
        if (duration < TimeSpan.FromMinutes(settings.MinDurationMinutes))
        {
            return $"Bookings must be at least {settings.MinDurationMinutes} minutes.";
        }
        if (duration > TimeSpan.FromMinutes(settings.MaxDurationMinutes))
        {
            var max = TimeSpan.FromMinutes(settings.MaxDurationMinutes);
            var maxText = max.TotalHours < 48 ? $"{max.TotalHours:0.#} hours" : $"{max.TotalDays:0.#} days";
            return $"Bookings cannot exceed {maxText}.";
        }
        if (end <= now)
        {
            return "Bookings cannot end in the past.";
        }
        if (start > now.AddDays(settings.BookingWindowDays))
        {
            return $"Bookings can be made at most {settings.BookingWindowDays} days in advance.";
        }
        return null;
    }

    /// <summary>
    /// Expands a series from its first occurrence, keeping the *local wall-clock time* in the
    /// location's zone across DST transitions (FR-4.10).
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> ExpandSeries(
        DateTimeOffset firstStart, DateTimeOffset firstEnd, SeriesFrequency frequency, int count, string timezone)
    {
        var zone = DateTimeZoneProviders.Tzdb[timezone];
        var localStart = Instant.FromDateTimeOffset(firstStart).InZone(zone).LocalDateTime;
        var localEnd = Instant.FromDateTimeOffset(firstEnd).InZone(zone).LocalDateTime;
        var step = frequency == SeriesFrequency.Daily ? Period.FromDays(1) : Period.FromWeeks(1);

        var occurrences = new List<(DateTimeOffset, DateTimeOffset)>(count);
        for (var i = 0; i < count; i++)
        {
            occurrences.Add((
                localStart.InZoneLeniently(zone).ToInstant().ToDateTimeOffset(),
                localEnd.InZoneLeniently(zone).ToInstant().ToDateTimeOffset()));
            localStart += step;
            localEnd += step;
        }
        return occurrences;
    }

    public static bool IsSnapped(DateTimeOffset value)
        => value.Minute % SnapMinutes == 0 && value.Second == 0 && value.Millisecond == 0;
}
