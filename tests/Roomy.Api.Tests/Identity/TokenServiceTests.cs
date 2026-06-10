using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Roomy.Api.Identity;

namespace Roomy.Api.Tests.Identity;

public class TokenServiceTests
{
    private static TokenService CreateService() => new(Options.Create(new JwtOptions
    {
        SigningKey = "unit-test-signing-key-with-32-bytes!!",
    }));

    [Fact]
    public void Access_token_carries_identity_and_tenant_claims()
    {
        var user = new User
        {
            Email = "a@b.test",
            Name = "A",
            Role = UserRole.FacilityManager,
            TenantId = Guid.NewGuid(),
        };

        var (token, expiresAt) = CreateService().CreateAccessToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(user.TenantId.ToString(), jwt.Claims.Single(c => c.Type == TokenService.TenantClaim).Value);
        Assert.Contains(jwt.Claims, c => c.Value == nameof(UserRole.FacilityManager));
        Assert.True(expiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Refresh_token_hash_matches_static_hash_of_raw_value()
    {
        var (raw, hash, expiresAt) = CreateService().CreateRefreshToken();

        Assert.Equal(TokenService.HashRefreshToken(raw), hash);
        Assert.NotEqual(raw, hash);
        Assert.True(expiresAt > DateTimeOffset.UtcNow.AddDays(13));
    }

    [Fact]
    public void Refresh_tokens_are_unique()
    {
        var service = CreateService();
        Assert.NotEqual(service.CreateRefreshToken().Raw, service.CreateRefreshToken().Raw);
    }
}

public class RefreshTokenTests
{
    [Fact]
    public void Active_only_when_unrevoked_and_unexpired()
    {
        var token = new RefreshToken { TokenHash = "h", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        Assert.True(token.IsActive);

        token.RevokedAt = DateTimeOffset.UtcNow;
        Assert.False(token.IsActive);

        var expired = new RefreshToken { TokenHash = "h", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        Assert.False(expired.IsActive);
    }
}
