using System.ComponentModel.DataAnnotations;

namespace MondShield.Infrastructure.Identity;

/// <summary>Strongly-typed JWT configuration, bound from the "Jwt" config section.</summary>
public sealed class JwtSettings
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC signing key. Must be at least 32 bytes for HS256. Supply via secrets/env in production.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 7;
}
