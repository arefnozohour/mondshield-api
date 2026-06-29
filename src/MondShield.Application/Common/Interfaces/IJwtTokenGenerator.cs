namespace MondShield.Application.Common.Interfaces;

/// <summary>Generates signed JWT access tokens and opaque refresh tokens.</summary>
public interface IJwtTokenGenerator
{
    AccessToken GenerateAccessToken(Guid userId, string email, IEnumerable<string> roles);

    /// <summary>Cryptographically-random opaque refresh token (not a JWT).</summary>
    string GenerateRefreshToken();
}

/// <summary>A signed JWT and the moment it expires (UTC).</summary>
public readonly record struct AccessToken(string Token, DateTime ExpiresAtUtc);
