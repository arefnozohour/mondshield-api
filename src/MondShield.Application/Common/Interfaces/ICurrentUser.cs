namespace MondShield.Application.Common.Interfaces;

/// <summary>
/// Ambient information about the caller, resolved from the JWT on the current request.
/// Implemented in the API layer over <c>IHttpContextAccessor</c>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user's id, or null for anonymous requests.</summary>
    Guid? UserId { get; }

    string? Email { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(string role);
}
