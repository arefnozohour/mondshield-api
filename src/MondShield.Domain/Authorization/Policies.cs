namespace MondShield.Domain.Authorization;

/// <summary>
/// Named authorization policies referenced by <c>[Authorize(Policy = ...)]</c> on
/// controllers/actions and registered in the API composition root.
/// </summary>
public static class Policies
{
    /// <summary>Requires the <see cref="Roles.Admin"/> role.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Requires any authenticated MondShield user (Trader or Admin).</summary>
    public const string AuthenticatedUser = "AuthenticatedUser";
}
