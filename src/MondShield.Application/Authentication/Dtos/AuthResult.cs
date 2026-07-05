namespace MondShield.Application.Authentication.Dtos;

/// <summary>
/// The token pair plus identity context returned on successful register/login/refresh.
/// </summary>
public sealed record AuthResult
{
    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    public required string FullName { get; init; }

    public required string AccessToken { get; init; }

    public required DateTime AccessTokenExpiresAt { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTime RefreshTokenExpiresAt { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }
}
