using Roomy.Api.Common.Persistence;

namespace Roomy.Api.Identity;

public sealed class User : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public required string Email { get; set; }

    public required string Name { get; set; }

    public UserRole Role { get; set; } = UserRole.Member;

    public UserStatus Status { get; set; } = UserStatus.Active;

    public AuthSource AuthSource { get; set; } = AuthSource.Local;

    public string? PasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum UserRole
{
    Member = 0,
    FacilityManager = 1,
    TenantAdmin = 2,
}

public enum UserStatus
{
    Active = 0,
    Inactive = 1,
}

public enum AuthSource
{
    Local = 0,
    Ldap = 1,
}
