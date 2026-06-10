using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Roomy.Api.Common.Persistence;
using Roomy.Api.Identity;
using Roomy.Api.Rooms;

namespace Roomy.Api.Bookings;

public static class BookingEndpoints
{
    /// <summary>Booking times snap to 15-minute increments (FR-4.5).</summary>
    private const int SnapMinutes = 15;

    /// <summary>Default policy ceiling until per-tenant policies land (FR-5.3).</summary>
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(8);

    public static RouteGroupBuilder MapBookingEndpoints(this RouteGroupBuilder group)
    {
        var bookings = group.MapGroup("/bookings").RequireAuthorization();

        bookings.MapGet("/", ListAsync);
        bookings.MapPost("/", CreateAsync);
        bookings.MapPost("/{id:guid}/cancel", CancelAsync);

        return group;
    }

    public sealed record CreateBookingRequest(
        Guid RoomId, string Title, DateTimeOffset StartAt, DateTimeOffset EndAt, int? AttendeeCount);

    public sealed record BookingDto(
        Guid Id, Guid RoomId, string Title, DateTimeOffset StartAt, DateTimeOffset EndAt,
        string Status, string OrganizerName, bool IsMine);

    private static async Task<IResult> ListAsync(
        Guid locationId, DateTimeOffset from, DateTimeOffset to,
        RoomyDbContext db, ClaimsPrincipal principal)
    {
        var userId = GetUserId(principal);

        var items = await db.Bookings
            .Where(b => b.Room!.LocationId == locationId
                && b.StartAt < to && b.EndAt > from
                && (b.Status == BookingStatus.PendingApproval
                    || b.Status == BookingStatus.Confirmed
                    || b.Status == BookingStatus.CheckedIn))
            .OrderBy(b => b.StartAt)
            .Select(b => new BookingDto(
                b.Id, b.RoomId, b.Title, b.StartAt, b.EndAt,
                b.Status.ToString(), b.Organizer!.Name, b.OrganizerId == userId))
            .ToListAsync();

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        CreateBookingRequest request, RoomyDbContext db, ClaimsPrincipal principal)
    {
        var start = request.StartAt.ToUniversalTime();
        var end = request.EndAt.ToUniversalTime();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Problem("validation.title", "A booking title is required.");
        }

        if (!IsSnapped(start) || !IsSnapped(end))
        {
            return Problem("validation.snap", $"Times must align to {SnapMinutes}-minute increments.");
        }

        if (end <= start)
        {
            return Problem("validation.range", "End time must be after the start time.");
        }

        if (end - start > MaxDuration)
        {
            return Problem("validation.max_duration", $"Bookings cannot exceed {MaxDuration.TotalHours} hours.");
        }

        if (end <= DateTimeOffset.UtcNow)
        {
            return Problem("validation.past", "Bookings cannot end in the past.");
        }

        var room = await db.Rooms.FirstOrDefaultAsync(r =>
            r.Id == request.RoomId && r.Status == RoomStatus.Active);
        if (room is null)
        {
            return Results.NotFound();
        }

        var booking = new Booking
        {
            RoomId = room.Id,
            OrganizerId = GetUserId(principal),
            Title = request.Title.Trim(),
            StartAt = start,
            EndAt = end,
            AttendeeCount = Math.Max(1, request.AttendeeCount ?? 1),
            Status = BookingStatus.Confirmed,
        };
        db.Bookings.Add(booking);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            // The bookings_no_overlap constraint is the source of truth for FR-4.3:
            // first committed wins, the loser gets a conflict (FR-4.6).
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "booking_conflict",
                detail: "That room is already booked for an overlapping time.");
        }

        return Results.Created(
            $"/api/v1/bookings/{booking.Id}",
            new BookingDto(
                booking.Id, booking.RoomId, booking.Title, booking.StartAt, booking.EndAt,
                booking.Status.ToString(), principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? "", true));
    }

    private static async Task<IResult> CancelAsync(
        Guid id, RoomyDbContext db, ClaimsPrincipal principal)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
        {
            return Results.NotFound();
        }

        var role = principal.FindFirstValue(ClaimTypes.Role);
        var isStaff = role is nameof(UserRole.TenantAdmin) or nameof(UserRole.FacilityManager);
        if (booking.OrganizerId != GetUserId(principal) && !isStaff)
        {
            return Results.NotFound();
        }

        if (booking.Status is not (BookingStatus.PendingApproval or BookingStatus.Confirmed))
        {
            return Problem("validation.state", "Only upcoming bookings can be cancelled.");
        }

        booking.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static Guid GetUserId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Token has no subject claim."));

    private static bool IsSnapped(DateTimeOffset value)
        => value.Minute % SnapMinutes == 0 && value.Second == 0 && value.Millisecond == 0;

    private static IResult Problem(string code, string detail)
        => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: code, detail: detail);
}
