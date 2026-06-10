using Microsoft.EntityFrameworkCore;
using Roomy.Api.Common.Persistence;
using Roomy.Api.Rooms;

namespace Roomy.Api.Locations;

public static class LocationEndpoints
{
    public static RouteGroupBuilder MapLocationEndpoints(this RouteGroupBuilder group)
    {
        var locations = group.MapGroup("/locations").RequireAuthorization();

        locations.MapGet("/", ListLocationsAsync);
        locations.MapGet("/{id:guid}/rooms", ListRoomsAsync);
        locations.MapPost("/", CreateLocationAsync).RequireAuthorization("TenantAdmin");
        locations.MapPost("/{id:guid}/rooms", CreateRoomAsync).RequireAuthorization("Staff");

        var rooms = group.MapGroup("/rooms").RequireAuthorization("Staff");
        rooms.MapPatch("/{id:guid}", UpdateRoomAsync);

        return group;
    }

    public sealed record CreateLocationRequest(string Name, string Timezone, string? Address);
    public sealed record CreateRoomRequest(
        string Name, int Capacity, string? Floor, bool RequiresApproval, bool CheckinRequired);
    public sealed record UpdateRoomRequest(
        string? Name, int? Capacity, string? Floor, bool? RequiresApproval, bool? CheckinRequired, string? Status);

    public sealed record LocationDto(Guid Id, string Name, string Timezone, string? Address);

    public sealed record RoomDto(
        Guid Id, string Name, int Capacity, string? Floor,
        bool RequiresApproval, bool CheckinRequired, string Status);

    private static async Task<IResult> ListLocationsAsync(RoomyDbContext db)
        => Results.Ok(await db.Locations
            .OrderBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.Name, l.Timezone, l.Address))
            .ToListAsync());

    private static async Task<IResult> ListRoomsAsync(Guid id, RoomyDbContext db)
    {
        if (!await db.Locations.AnyAsync(l => l.Id == id))
        {
            return Results.NotFound();
        }

        return Results.Ok(await db.Rooms
            .Where(r => r.LocationId == id && r.Status != RoomStatus.Retired)
            .OrderBy(r => r.Name)
            .Select(r => new RoomDto(
                r.Id, r.Name, r.Capacity, r.Floor,
                r.RequiresApproval, r.CheckinRequired, r.Status.ToString()))
            .ToListAsync());
    }

    private static async Task<IResult> CreateLocationAsync(CreateLocationRequest request, RoomyDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Problem("A location name is required.");
        }
        try
        {
            _ = NodaTime.DateTimeZoneProviders.Tzdb[request.Timezone];
        }
        catch (NodaTime.TimeZones.DateTimeZoneNotFoundException)
        {
            return Problem($"'{request.Timezone}' is not a valid IANA time zone.");
        }
        var location = new Location
        {
            Name = request.Name.Trim(), Timezone = request.Timezone, Address = request.Address,
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/locations/{location.Id}",
            new LocationDto(location.Id, location.Name, location.Timezone, location.Address));
    }

    private static async Task<IResult> CreateRoomAsync(Guid id, CreateRoomRequest request, RoomyDbContext db)
    {
        if (!await db.Locations.AnyAsync(l => l.Id == id))
        {
            return Results.NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.Name) || request.Capacity < 1)
        {
            return Problem("A room name and a capacity of at least 1 are required.");
        }
        var room = new Room
        {
            LocationId = id, Name = request.Name.Trim(), Capacity = request.Capacity,
            Floor = request.Floor, RequiresApproval = request.RequiresApproval,
            CheckinRequired = request.CheckinRequired,
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/rooms/{room.Id}", new RoomDto(
            room.Id, room.Name, room.Capacity, room.Floor,
            room.RequiresApproval, room.CheckinRequired, room.Status.ToString()));
    }

    private static async Task<IResult> UpdateRoomAsync(Guid id, UpdateRoomRequest request, RoomyDbContext db)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id);
        if (room is null)
        {
            return Results.NotFound();
        }
        if (request.Status is not null)
        {
            if (!Enum.TryParse<RoomStatus>(request.Status, true, out var status))
            {
                return Problem("Unknown room status.");
            }
            room.Status = status;
        }
        if (request.Name is not null)
        {
            room.Name = request.Name.Trim();
        }
        if (request.Capacity is { } capacity)
        {
            if (capacity < 1)
            {
                return Problem("Capacity must be at least 1.");
            }
            room.Capacity = capacity;
        }
        room.Floor = request.Floor ?? room.Floor;
        room.RequiresApproval = request.RequiresApproval ?? room.RequiresApproval;
        room.CheckinRequired = request.CheckinRequired ?? room.CheckinRequired;
        await db.SaveChangesAsync();
        return Results.Ok(new RoomDto(room.Id, room.Name, room.Capacity, room.Floor,
            room.RequiresApproval, room.CheckinRequired, room.Status.ToString()));
    }

    private static IResult Problem(string detail)
        => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, detail: detail);
}
