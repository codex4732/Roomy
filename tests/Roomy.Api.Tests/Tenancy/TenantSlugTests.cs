using Roomy.Api.Common.Tenancy;

namespace Roomy.Api.Tests.Tenancy;

public class TenantSlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-corp")]
    [InlineData("a1b")]
    [InlineData("123")]
    public void Accepts_valid_slugs(string slug) =>
        Assert.True(TenantSlug.IsValid(slug));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("Acme")]
    [InlineData("acme corp")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme.corp")]
    public void Rejects_invalid_slugs(string? slug) =>
        Assert.False(TenantSlug.IsValid(slug));

    [Fact]
    public void Rejects_slugs_over_max_length() =>
        Assert.False(TenantSlug.IsValid(new string('a', TenantSlug.MaxLength + 1)));
}
