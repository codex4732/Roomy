using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Roomy.Api.Common.Persistence;
using Roomy.Api.Common.Tenancy;
using Roomy.Api.Identity;
using Roomy.Api.Rooms;

namespace Roomy.Api.Bookings;

public static class BookingEndpoints
{
    public static RouteGroupBuilder MapBookingEndpoints(this RouteGroupBuilder group)
    {
        var bookings = group.MapGroup("/bookings").RequireAuthorization();

        bookings.MapGet("/", ListAsync);
        bookings.MapGet("/mine", ListMineAsync);
        bookings.MapPost("/", CreateAsync);
        bookings.MapPost("/series", CreateSeriesAsync);
        bookings.MapPost("/series/{id:guid}/cancel", CancelSeriesAsync);
        bookings.MapPost("/{id:guid}/cancel", CancelAsync);
        bookings.MapPost("/{id:guid}/check-in", CheckInAsync);
        bookings.MapPost("/{id:guid}/end", EndAsync);

        var approvals = group.MapGroup("/approvals").RequireAuthorization("Staff");
        approvals.MapGet("/", ListPendingAsync);
        approvals.MapPost("/{id:guid}/approve", (Guid id, RoomyDbContext db) => DecideAsync(id, true, null, db));
        approvals.MapPost("/{id:guid}/decline", (Guid id, DeclineRequest req, RoomyDbContext db) => DecideAsync(id, false, req.Reason, db));

        group.MapGet("/settings", GetSettingsAsync).RequireAuthorization();
        group.MapPut("/settings", UpdateSettingsAsync).RequireAuthorization("TenantAdmin");
        return group;
    }

    public sealed record CreateBookingRequest(
        Guid RoomId, string Title, DateTimeOffset StartAt, DateTimeOffset EndAt, int? AttendeeCount,
        string? SetupNotes, List<string>? Participants);

    public sealed record CreateSeriesRequest(
        Guid RoomId, string Title, DateTimeOffset StartAt, DateTimeOffset EndAt,
        string Frequency, int Count, bool SkipConflicts,
        string? SetupNotes, List<string>? Participants);

    public sealed record DeclineRequest(string? Reason);

    public sealed record BookingDto(
        Guid Id, Guid RoomId, string Title, DateTimeOffset StartAt, DateTimeOffset EndAt,
        string Status, string OrganizerName, bool IsMine, Guid? SeriesId, bool CheckinRequired,
        string? SetupNotes, List<string> Participants);

    public sealed record SeriesResult(Guid SeriesId, List<BookingDto> Created, List<DateTimeOffset> Skipped);

    private static async Task<IResult> GetSettingsAsync(RoomyDbContext db, ITenantContext tenant)
        => Results.Ok((await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId)).Settings);

    public sealed record MineDto(
        Guid Id, Guid RoomId, string RoomName, string LocationName, string Title,
        DateTimeOffset StartAt, DateTimeOffset EndAt, string Status, Guid? SeriesId,
        bool CheckinRequired, string? SetupNotes, List<string> Participants);

    private static async Task<IResult> ListMineAsync(RoomyDbContext db, ClaimsPrincipal principal)
    {
        var userId = GetUserId(principal);
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        return Results.Ok(await db.Bookings
            .Where(b => b.OrganizerId == userId && b.EndAt > cutoff)
            .OrderBy(b => b.StartAt)
            .Take(200)
            .Select(b => new MineDto(b.Id, b.RoomId, b.Room!.Name, b.Room!.Location!.Name, b.Title,
                b.StartAt, b.EndAt, b.Status.ToString(), b.SeriesId, b.Room!.CheckinRequired,
                b.SetupNotes, b.Participants))
            .ToListAsync());
    }

    private static async Task<IResult> UpdateSettingsAsync(
        Tenants.TenantSettings request, RoomyDbContext db, ITenantContext tenant)
    {
        if (request.BookingWindowDays is < 1 or > 365
            || request.MaxDurationMinutes is < 15 or > 44640
            || request.MinDurationMinutes < 5 || request.MinDurationMinutes > request.MaxDurationMinutes
            || request.MaxActiveBookingsPerUser is < 1 or > 100
            || request.CheckinGraceMinutes is < 5 or > 30
            || request.ApprovalExpiryHours is < 1 or > 168)
        {
            return Problem("One or more policy values are out of range.");
        }
        var row = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId);
        row.Settings = request;
        await db.SaveChangesAsync();
        return Results.Ok(row.Settings);
    }

    private static async Task<IResult> ListAsync(
        Guid locationId, DateTimeOffset from, DateTimeOffset to, RoomyDbContext db, ClaimsPrincipal principal)
    {
        var userId = GetUserId(principal);
        return Results.Ok(await db.Bookings
            .Where(b => b.Room!.LocationId == locationId && b.StartAt < to && b.EndAt > from
                && (b.Status == BookingStatus.PendingApproval || b.Status == BookingStatus.Confirmed
                    || b.Status == BookingStatus.CheckedIn))
            .OrderBy(b => b.StartAt)
            .Select(b => new BookingDto(b.Id, b.RoomId, b.Title, b.StartAt, b.EndAt, b.Status.ToString(),
                b.Organizer!.Name, b.OrganizerId == userId, b.SeriesId, b.Room!.CheckinRequired,
                b.SetupNotes, b.Participants))
            .ToListAsync());
    }

    private static async Task<IResult> CreateAsync(
        CreateBookingRequest request, RoomyDbContext db, ITenantContext tenant, ClaimsPrincipal principal)
    {
        var prepared = await PrepareAsync(request.RoomId, request.Title, request.StartAt, request.EndAt,
            request.SetupNotes, request.Participants, db, tenant, principal);
        if (prepared.Error is not null)
        {
            return prepared.Error;
        }

        if (await Blackouts.BlackoutEndpoints.IsBlackedOutAsync(
            db, prepared.Room!.Id, prepared.Room!.LocationId, prepared.Start, prepared.End))
        {
            return Problem("The room is blocked by a blackout period during that time.");
        }

        var booking = NewBooking(prepared, request.AttendeeCount);
        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsOverlap(ex))
        {
            return Conflict();
        }
        return Results.Created($"/api/v1/bookings/{booking.Id}", ToDto(booking, prepared.Room!, principal));
    }

    private static async Task<IResult> CreateSeriesAsync(
        CreateSeriesRequest request, RoomyDbContext db, ITenantContext tenant, ClaimsPrincipal principal)
    {
        if (!Enum.TryParse<SeriesFrequency>(request.Frequency, true, out var frequency))
        {
            return Problem("Frequency must be 'daily' or 'weekly'.");
        }
        var maxCount = frequency == SeriesFrequency.Daily ? BookingRules.MaxSeriesOccurrences : 26;
        if (request.Count < 2 || request.Count > maxCount)
        {
            return Problem($"Series must have between 2 and {maxCount} occurrences.");
        }

        var prepared = await PrepareAsync(request.RoomId, request.Title, request.StartAt, request.EndAt,
            request.SetupNotes, request.Participants, db, tenant, principal);
        if (prepared.Error is not null)
        {
            return prepared.Error;
        }

        var location = await db.Locations.FirstAsync(l => l.Id == prepared.Room!.LocationId);
        var occurrences = BookingRules.ExpandSeries(
            prepared.Start, prepared.End, frequency, request.Count, location.Timezone);

        var series = new BookingSeries
        {
            RoomId = prepared.Room!.Id, OrganizerId = prepared.UserId,
            Title = prepared.Title, Frequency = frequency, Count = request.Count,
        };
        db.BookingSeries.Add(series);

        var created = new List<Booking>();
        var skipped = new List<DateTimeOffset>();

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        foreach (var (start, end) in occurrences)
        {
            if (await Blackouts.BlackoutEndpoints.IsBlackedOutAsync(
                db, prepared.Room!.Id, prepared.Room!.LocationId, start, end))
            {
                skipped.Add(start);
                continue;
            }
            var booking = NewBooking(prepared with { Start = start, End = end }, null);
            booking.SeriesId = series.Id;
            db.Bookings.Add(booking);
            await tx.CreateSavepointAsync("occ");
            try
            {
                await db.SaveChangesAsync();
                created.Add(booking);
            }
            catch (DbUpdateException ex) when (IsOverlap(ex))
            {
                await tx.RollbackToSavepointAsync("occ");
                db.Entry(booking).State = EntityState.Detached;
                skipped.Add(start);
            }
        }

        if (created.Count == 0 || (skipped.Count > 0 && !request.SkipConflicts))
        {
            await tx.RollbackAsync();
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "series_conflicts",
                detail: $"{skipped.Count} of {request.Count} occurrences conflict.",
                extensions: new Dictionary<string, object?> { ["conflictDates"] = skipped });
        }

        await tx.CommitAsync();
        return Results.Created($"/api/v1/bookings/series/{series.Id}",
            new SeriesResult(series.Id, created.Select(b => ToDto(b, prepared.Room!, principal)).ToList(), skipped));
    }

    private static async Task<IResult> CancelSeriesAsync(Guid id, RoomyDbContext db, ClaimsPrincipal principal)
    {
        var series = await db.BookingSeries.FirstOrDefaultAsync(s => s.Id == id);
        if (series is null || (series.OrganizerId != GetUserId(principal) && !IsStaff(principal)))
        {
            return Results.NotFound();
        }
        var now = DateTimeOffset.UtcNow;
        await db.Bookings
            .Where(b => b.SeriesId == id && b.StartAt > now
                && (b.Status == BookingStatus.PendingApproval || b.Status == BookingStatus.Confirmed))
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BookingStatus.Cancelled));
        return Results.NoContent();
    }

    private static async Task<IResult> CancelAsync(Guid id, RoomyDbContext db, ClaimsPrincipal principal)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null || (booking.OrganizerId != GetUserId(principal) && !IsStaff(principal)))
        {
            return Results.NotFound();
        }
        if (booking.Status is not (BookingStatus.PendingApproval or BookingStatus.Confirmed))
        {
            return Problem("Only upcoming bookings can be cancelled.");
        }
        booking.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> CheckInAsync(
        Guid id, RoomyDbContext db, ITenantContext tenant, ClaimsPrincipal principal)
    {
        var booking = await db.Bookings.Include(b => b.Room).FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null || booking.OrganizerId != GetUserId(principal))
        {
            return Results.NotFound();
        }
        if (booking.Status != BookingStatus.Confirmed)
        {
            return Problem("Only confirmed bookings can be checked in.");
        }
        var settings = (await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId)).Settings;
        var now = DateTimeOffset.UtcNow;
        var windowOpen = booking.StartAt.AddMinutes(-10);
        var windowClose = booking.StartAt.AddMinutes(settings.CheckinGraceMinutes);
        if (now < windowOpen || now > windowClose)
        {
            return Problem($"Check-in opens 10 minutes before start and closes {settings.CheckinGraceMinutes} minutes after.");
        }
        booking.Status = BookingStatus.CheckedIn;
        booking.CheckedInAt = now;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> EndAsync(Guid id, RoomyDbContext db, ClaimsPrincipal principal)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null || (booking.OrganizerId != GetUserId(principal) && !IsStaff(principal)))
        {
            return Results.NotFound();
        }
        if (booking.Status != BookingStatus.CheckedIn)
        {
            return Problem("Only checked-in meetings can be ended.");
        }
        booking.Status = BookingStatus.Completed;
        var now = DateTimeOffset.UtcNow;
        if (now < booking.EndAt)
        {
            // Ending before the official start (early check-in) collapses to an empty range.
            booking.EndAt = now > booking.StartAt ? now : booking.StartAt;
        }
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ListPendingAsync(RoomyDbContext db)
        => Results.Ok(await db.Bookings
            .Where(b => b.Status == BookingStatus.PendingApproval)
            .OrderBy(b => b.StartAt)
            .Select(b => new
            {
                b.Id, b.Title, b.StartAt, b.EndAt, b.SeriesId, b.SetupNotes, b.Participants,
                Room = b.Room!.Name, Organizer = b.Organizer!.Name, b.CreatedAt,
            })
            .ToListAsync());

    private static async Task<IResult> DecideAsync(Guid id, bool approve, string? reason, RoomyDbContext db)
    {
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
        {
            return Results.NotFound();
        }
        if (booking.Status != BookingStatus.PendingApproval)
        {
            return Problem("This request has already been decided.");
        }
        booking.Status = approve ? BookingStatus.Confirmed : BookingStatus.Declined;
        booking.CancelReason = approve ? null : reason;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private sealed record Prepared(
        IResult? Error, Room? Room, Guid UserId, string Title, DateTimeOffset Start, DateTimeOffset End,
        BookingStatus InitialStatus, string? SetupNotes, List<string> Participants);

    private static async Task<Prepared> PrepareAsync(
        Guid roomId, string title, DateTimeOffset startAt, DateTimeOffset endAt,
        string? setupNotes, List<string>? participants,
        RoomyDbContext db, ITenantContext tenant, ClaimsPrincipal principal)
    {
        static Prepared Fail(IResult error) => new(error, null, Guid.Empty, "", default, default, default, null, []);

        if (string.IsNullOrWhiteSpace(title))
        {
            return Fail(Problem("A booking title is required."));
        }

        var cleanParticipants = (participants ?? [])
            .Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList();
        if (cleanParticipants.Count > 50 || cleanParticipants.Any(p => p.Length > 200))
        {
            return Fail(Problem("Up to 50 participants, 200 characters each."));
        }
        if (setupNotes?.Length > 1000)
        {
            return Fail(Problem("Setup notes are limited to 1000 characters."));
        }

        var start = startAt.ToUniversalTime();
        var end = endAt.ToUniversalTime();
        var settings = (await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId)).Settings;
        var now = DateTimeOffset.UtcNow;

        if (BookingRules.Validate(start, end, now, settings) is { } violation)
        {
            return Fail(Problem(violation));
        }

        var userId = GetUserId(principal);
        var active = await db.Bookings.CountAsync(b => b.OrganizerId == userId && b.EndAt > now
            && (b.Status == BookingStatus.PendingApproval || b.Status == BookingStatus.Confirmed));
        if (active >= settings.MaxActiveBookingsPerUser)
        {
            return Fail(Problem($"You already have {active} active bookings (limit {settings.MaxActiveBookingsPerUser})."));
        }

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId && r.Status == RoomStatus.Active);
        if (room is null)
        {
            return Fail(Results.NotFound());
        }

        return new Prepared(null, room, userId, title.Trim(), start, end,
            room.RequiresApproval ? BookingStatus.PendingApproval : BookingStatus.Confirmed,
            string.IsNullOrWhiteSpace(setupNotes) ? null : setupNotes.Trim(), cleanParticipants);
    }

    private static Booking NewBooking(Prepared prepared, int? attendees) => new()
    {
        RoomId = prepared.Room!.Id,
        OrganizerId = prepared.UserId,
        Title = prepared.Title,
        StartAt = prepared.Start,
        EndAt = prepared.End,
        AttendeeCount = Math.Max(1, attendees ?? Math.Max(1, prepared.Participants.Count)),
        Status = prepared.InitialStatus,
        SetupNotes = prepared.SetupNotes,
        Participants = prepared.Participants,
    };

    private static BookingDto ToDto(Booking booking, Room room, ClaimsPrincipal principal)
        => new(booking.Id, booking.RoomId, booking.Title, booking.StartAt, booking.EndAt,
            booking.Status.ToString(), principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? "",
            true, booking.SeriesId, room.CheckinRequired, booking.SetupNotes, booking.Participants);

    private static bool IsOverlap(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation };

    private static bool IsStaff(ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Role) is nameof(UserRole.TenantAdmin) or nameof(UserRole.FacilityManager);

    private static Guid GetUserId(ClaimsPrincipal principal)
        => Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Token has no subject claim."));

    private static IResult Conflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict, title: "booking_conflict",
        detail: "That room is already booked for an overlapping time.");

    private static IResult Problem(string detail)
        => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "policy_violation", detail: detail);
}
