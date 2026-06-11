using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Roomy.Api.Common.Persistence;

namespace Roomy.Api.Identity;

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        var users = group.MapGroup("/users").RequireAuthorization("TenantAdmin");
        users.MapGet("/", ListAsync);
        users.MapPost("/", CreateAsync);
        users.MapPatch("/{id:guid}", UpdateAsync);
        return group;
    }

    public sealed record CreateUserRequest(string Email, string Name, string Role, string Password);
    public sealed record UpdateUserRequest(string? Role, string? Status);
    public sealed record UserAdminDto(Guid Id, string Email, string Name, string Role, string Status, int NoShowCount);

    private static async Task<IResult> ListAsync(RoomyDbContext db)
        => Results.Ok(await db.Users.OrderBy(u => u.Name)
            .Select(u => new UserAdminDto(u.Id, u.Email, u.Name, u.Role.ToString(), u.Status.ToString(), u.NoShowCount))
            .ToListAsync());

    private static async Task<IResult> CreateAsync(
        CreateUserRequest request, RoomyDbContext db, IPasswordHasher<User> hasher)
    {
        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
        {
            return Problem("Role must be Member, FacilityManager, or TenantAdmin.");
        }
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Name)
            || request.Password.Length < 12)
        {
            return Problem("Email, name, and a password of at least 12 characters are required.");
        }
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return Results.Conflict(new { detail = "A user with that email already exists." });
        }
        var user = new User { Email = email, Name = request.Name.Trim(), Role = role };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/users/{user.Id}",
            new UserAdminDto(user.Id, user.Email, user.Name, user.Role.ToString(), user.Status.ToString(), 0));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateUserRequest request, RoomyDbContext db)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return Results.NotFound();
        }
        if (request.Role is not null)
        {
            if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            {
                return Problem("Unknown role.");
            }
            user.Role = role;
        }
        if (request.Status is not null)
        {
            if (!Enum.TryParse<UserStatus>(request.Status, true, out var status))
            {
                return Problem("Unknown status.");
            }
            user.Status = status;
        }
        await db.SaveChangesAsync();
        return Results.Ok(new UserAdminDto(user.Id, user.Email, user.Name, user.Role.ToString(), user.Status.ToString(), user.NoShowCount));
    }

    private static IResult Problem(string detail)
        => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, detail: detail);
}
