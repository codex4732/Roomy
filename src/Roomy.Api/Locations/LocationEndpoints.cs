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

        return group;
    }

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
}
