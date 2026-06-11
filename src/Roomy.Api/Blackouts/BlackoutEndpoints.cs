using Microsoft.EntityFrameworkCore;
using Roomy.Api.Bookings;
using Roomy.Api.Common.Persistence;

namespace Roomy.Api.Blackouts;

public static class BlackoutEndpoints
{
    public static RouteGroupBuilder MapBlackoutEndpoints(this RouteGroupBuilder group)
    {
        var blackouts = group.MapGroup("/blackouts");

        // Members need read access so the availability grid can render blocked slots.
        blackouts.MapGet("/", ListAsync).RequireAuthorization();
        blackouts.MapPost("/", CreateAsync).RequireAuthorization("Staff");
        blackouts.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization("Staff");

        return group;
    }

    public sealed record CreateBlackoutRequest(
        Guid LocationId, Guid? RoomId, string Reason, DateTimeOffset StartAt, DateTimeOffset EndAt);

    public sealed record BlackoutDto(
        Guid Id, Guid LocationId, Guid? RoomId, string Reason, DateTimeOffset StartAt, DateTimeOffset EndAt);

    private static async Task<IResult> ListAsync(
        Guid locationId, DateTimeOffset from, DateTimeOffset to, RoomyDbContext db)
        => Results.Ok(await db.Blackouts
            .Where(b => b.LocationId == locationId && b.StartAt < to && b.EndAt > from)
            .OrderBy(b => b.StartAt)
            .Select(b => new BlackoutDto(b.Id, b.LocationId, b.RoomId, b.Reason, b.StartAt, b.EndAt))
            .ToListAsync());

    private static async Task<IResult> CreateAsync(CreateBlackoutRequest request, RoomyDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.EndAt <= request.StartAt)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: "A reason and a valid time range are required.");
        }
        if (!await db.Locations.AnyAsync(l => l.Id == request.LocationId)
            || (request.RoomId is { } roomId
                && !await db.Rooms.AnyAsync(r => r.Id == roomId && r.LocationId == request.LocationId)))
        {
            return Results.NotFound();
        }

        var blackout = new BlackoutPeriod
        {
            LocationId = request.LocationId,
            RoomId = request.RoomId,
            Reason = request.Reason.Trim(),
            StartAt = request.StartAt.ToUniversalTime(),
            EndAt = request.EndAt.ToUniversalTime(),
        };
        db.Blackouts.Add(blackout);

        // FR-5.4: cancel affected bookings (with the reason) when the blackout is confirmed.
        var affected = await db.Bookings
            .Where(b => b.StartAt < blackout.EndAt && b.EndAt > blackout.StartAt
                && (b.Status == BookingStatus.PendingApproval || b.Status == BookingStatus.Confirmed
                    || b.Status == BookingStatus.CheckedIn)
                && (blackout.RoomId != null ? b.RoomId == blackout.RoomId : b.Room!.LocationId == blackout.LocationId))
            .ToListAsync();
        foreach (var booking in affected)
        {
            booking.Status = BookingStatus.Cancelled;
            booking.CancelReason = $"Blackout: {blackout.Reason}";
        }

        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/blackouts/{blackout.Id}", new
        {
            blackout = new BlackoutDto(blackout.Id, blackout.LocationId, blackout.RoomId,
                blackout.Reason, blackout.StartAt, blackout.EndAt),
            cancelledBookings = affected.Count,
        });
    }

    private static async Task<IResult> DeleteAsync(Guid id, RoomyDbContext db)
    {
        var deleted = await db.Blackouts.Where(b => b.Id == id).ExecuteDeleteAsync();
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    /// <summary>Shared check used by booking creation paths.</summary>
    public static Task<bool> IsBlackedOutAsync(
        RoomyDbContext db, Guid roomId, Guid locationId, DateTimeOffset start, DateTimeOffset end)
        => db.Blackouts.AnyAsync(b => b.StartAt < end && b.EndAt > start
            && (b.RoomId == roomId || (b.RoomId == null && b.LocationId == locationId)));
}
