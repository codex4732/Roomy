using System.Text.RegularExpressions;

namespace Roomy.Api.Common.Tenancy;

/// <summary>
/// Tenant slug rules (PRD §4): used as the cloud subdomain and the self-hosted path segment,
/// so it must be a valid DNS label. Lowercase letters, digits, and interior hyphens; 3–40 chars.
/// </summary>
public static partial class TenantSlug
{
    public const int MinLength = 3;
    public const int MaxLength = 40;

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    public static bool IsValid(string? slug) =>
        slug is not null
        && slug.Length is >= MinLength and <= MaxLength
        && SlugPattern().IsMatch(slug);
}
