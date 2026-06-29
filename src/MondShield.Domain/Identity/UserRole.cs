namespace MondShield.Domain.Identity;

/// <summary>
/// The two roles in the system. Persisted as a string column and emitted as the JWT role
/// claim, so the names line up with <see cref="Authorization.Roles"/>.
/// </summary>
public enum UserRole
{
    /// <summary>Trader — owns a MondShield account, submits compensation requests.</summary>
    User = 0,

    /// <summary>Broker staff — review queue, approvals, payouts, cap monitoring.</summary>
    Admin = 1,
}
