using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Roomy.Api.Common.Persistence;

namespace Roomy.Api.Identity;

public static class AuthEndpoints
{
    private const string RefreshCookie = "roomy_refresh";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth");

        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/refresh", RefreshAsync);
        auth.MapPost("/logout", LogoutAsync);

        return group;
    }

    public sealed record LoginRequest(string Email, string Password);

    public sealed record AuthResponse(
        string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

    public sealed record UserDto(Guid Id, string Email, string Name, string Role);

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        RoomyDbContext db,
        TokenService tokens,
        IPasswordHasher<User> passwordHasher,
        HttpContext http)
    {
        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Email == request.Email.ToLowerInvariant() && u.Status == UserStatus.Active);

        // Verify against a dummy hash when the user is unknown so response timing
        // doesn't reveal which emails exist.
        if (user?.PasswordHash is null)
        {
            passwordHasher.VerifyHashedPassword(null!, DummyHash, request.Password);
            return Results.Unauthorized();
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }

        return await IssueTokensAsync(user, db, tokens, http);
    }

    private static async Task<IResult> RefreshAsync(
        RoomyDbContext db, TokenService tokens, HttpContext http)
    {
        if (http.Request.Cookies[RefreshCookie] is not { Length: > 0 } raw)
        {
            return Results.Unauthorized();
        }

        var hash = TokenService.HashRefreshToken(raw);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (stored is not { IsActive: true })
        {
            return Results.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Id == stored.UserId && u.Status == UserStatus.Active);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        stored.RevokedAt = DateTimeOffset.UtcNow;
        return await IssueTokensAsync(user, db, tokens, http);
    }

    private static async Task<IResult> LogoutAsync(RoomyDbContext db, HttpContext http)
    {
        if (http.Request.Cookies[RefreshCookie] is { Length: > 0 } raw)
        {
            var hash = TokenService.HashRefreshToken(raw);
            var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
            if (stored is not null)
            {
                stored.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        http.Response.Cookies.Delete(RefreshCookie, BuildCookieOptions(http));
        return Results.NoContent();
    }

    private static async Task<IResult> IssueTokensAsync(
        User user, RoomyDbContext db, TokenService tokens, HttpContext http)
    {
        var (access, expiresAt) = tokens.CreateAccessToken(user);
        var (rawRefresh, refreshHash, refreshExpiry) = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiry,
        });
        await db.SaveChangesAsync();

        var cookieOptions = BuildCookieOptions(http);
        cookieOptions.Expires = refreshExpiry;
        http.Response.Cookies.Append(RefreshCookie, rawRefresh, cookieOptions);

        return Results.Ok(new AuthResponse(
            access, expiresAt,
            new UserDto(user.Id, user.Email, user.Name, user.Role.ToString())));
    }

    private static CookieOptions BuildCookieOptions(HttpContext http) => new()
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
    };

    // A valid PBKDF2 hash of an unguessable value, used only to equalize timing.
    private const string DummyHash =
        "AQAAAAIAAYagAAAAEEYca5cANarhg+SBZvCxk+1HUikKVW0LceJcsImNUXm0K2U8oFAByYu6lXJzwESq+A==";
}
