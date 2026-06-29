namespace MondShield.Domain.Authorization;

/// <summary>
/// Canonical role names for the MondShield system. These are the single source of
/// truth for role strings — never hardcode role names inline in controllers, policies,
/// or seeding code.
/// </summary>
public static class Roles
{
    /// <summary>Broker staff: review queue, approve/reject, payout trigger, cap monitoring.</summary>
    public const string Admin = "Admin";

    /// <summary>Trader: views own account, balance composition, submits compensation requests.</summary>
    public const string User = "User";

    public static readonly IReadOnlyList<string> All = new[] { Admin, User };
}
