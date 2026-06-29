namespace MondShield.Domain.Identity;

/// <summary>
/// A login account. Deliberately minimal: email + hashed password + one role, plus a
/// rotating refresh token. No MFA, lockout, claims, or external logins.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stored lower-cased; unique. Used as the login identifier.</summary>
    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>PBKDF2 hash produced by the password hasher — never the raw password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>Current opaque refresh token (null once revoked or never issued).</summary>
    public string? RefreshToken { get; set; }

    public DateTime? RefreshTokenExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
