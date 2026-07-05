using MondShield.Application.Authentication.Dtos;
using MondShield.Application.Common.Models;

namespace MondShield.Application.Common.Interfaces;

/// <summary>
/// Port for identity/account operations. Implemented in Infrastructure on top of
/// ASP.NET Core Identity so the rest of the application never touches UserManager directly.
/// </summary>
public interface IIdentityService
{
    Task<Result<AuthResult>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<Result<AuthResult>> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    /// <summary>Invalidate the current refresh token for a user (logout / revoke).</summary>
    Task<Result> RevokeRefreshTokenAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Non-sensitive identity projection for a user. Null if not found.</summary>
    Task<UserSummary?> GetUserSummaryAsync(Guid userId, CancellationToken ct = default);
}
