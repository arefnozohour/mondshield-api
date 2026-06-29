using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MondShield.Application.Authentication.Dtos;
using MondShield.Application.Common.Interfaces;
using MondShield.Application.Common.Models;
using MondShield.Domain.Identity;
using MondShield.Infrastructure.Persistence;

namespace MondShield.Infrastructure.Identity;

/// <summary>
/// Lightweight implementation of <see cref="IIdentityService"/>: email + password login
/// with hashed passwords and refresh-token rotation. No ASP.NET Core Identity stack.
/// </summary>
public sealed class IdentityService : IIdentityService
{
    private readonly MondShieldDbContext _db;
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly JwtSettings _jwtSettings;

    public IdentityService(
        MondShieldDbContext db,
        IPasswordHasher<AppUser> passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IOptions<JwtSettings> jwtSettings)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _jwtSettings = jwtSettings.Value;
    }

    public async Task<Result<AuthResult>> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Result<AuthResult>.Failure("An account with this email already exists.");
        }

        var user = new AppUser
        {
            Email = email,
            FullName = request.FullName.Trim(),
            Role = UserRole.User,
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        _db.Users.Add(user);
        var result = await IssueTokensAsync(user, ct);
        return Result<AuthResult>.Success(result);
    }

    public async Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null || !VerifyPassword(user, request.Password))
        {
            // Deliberately uniform message to avoid user enumeration.
            return Result<AuthResult>.Failure("Invalid email or password.");
        }

        var result = await IssueTokensAsync(user, ct);
        return Result<AuthResult>.Success(result);
    }

    public async Task<Result<AuthResult>> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.RefreshToken == request.RefreshToken, ct);

        if (user is null ||
            user.RefreshTokenExpiresAtUtc is null ||
            user.RefreshTokenExpiresAtUtc <= DateTime.UtcNow)
        {
            return Result<AuthResult>.Failure("Invalid or expired refresh token.");
        }

        var result = await IssueTokensAsync(user, ct);
        return Result<AuthResult>.Success(result);
    }

    public async Task<Result> RevokeRefreshTokenAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Result.Failure("User not found.");
        }

        user.RefreshToken = null;
        user.RefreshTokenExpiresAtUtc = null;
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private bool VerifyPassword(AppUser user, string password)
    {
        var outcome = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (outcome == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
        }
        return outcome != PasswordVerificationResult.Failed;
    }

    /// <summary>Mints an access + refresh token pair, persisting the (rotated) refresh token.</summary>
    private async Task<AuthResult> IssueTokensAsync(AppUser user, CancellationToken ct)
    {
        var roles = new[] { user.Role.ToString() };

        var access = _tokenGenerator.GenerateAccessToken(user.Id, user.Email, roles);
        var refreshToken = _tokenGenerator.GenerateRefreshToken();
        var refreshExpiry = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenDays);

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAtUtc = refreshExpiry;
        await _db.SaveChangesAsync(ct);

        return new AuthResult
        {
            UserId = user.Id,
            Email = user.Email,
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAtUtc,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAt = refreshExpiry,
            Roles = roles,
        };
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
