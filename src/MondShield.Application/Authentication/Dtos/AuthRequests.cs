using System.ComponentModel.DataAnnotations;

namespace MondShield.Application.Authentication.Dtos;

/// <summary>Self-registration of a new trader account (assigned the User role).</summary>
public sealed record RegisterRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; init; } = string.Empty;

    [Required]
    public string FullName { get; init; } = string.Empty;
}

/// <summary>Email + password login.</summary>
public sealed record LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

/// <summary>Exchange a still-valid refresh token for a new access/refresh token pair.</summary>
public sealed record RefreshRequest
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}
