using Roomy.Api.Common.Persistence;

namespace Roomy.Api.Identity;

/// <summary>
/// Server-side record of an issued refresh token (technical design §7.1).
/// Only the SHA-256 hash of the token is stored; rotation revokes the old row.
/// </summary>
public sealed class RefreshToken : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
