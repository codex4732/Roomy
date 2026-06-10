namespace Roomy.Api.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "roomy";

    public string Audience { get; init; } = "roomy";

    /// <summary>Symmetric signing key, ≥ 32 bytes. Must be overridden outside Development.</summary>
    public string SigningKey { get; init; } = "";

    public int AccessTokenMinutes { get; init; } = 15;

    public int RefreshTokenDays { get; init; } = 14;
}
