namespace MondShield.Domain.Accounts;

/// <summary>
/// Onboarding/lifecycle status of a <see cref="ShieldAccount"/>, tracking the steps in
/// CLAUDE.md's onboarding flow: sign up (handled by the auth user, not here) → KYC review →
/// MT5 provisioning → activation deposit.
/// </summary>
public enum AccountStatus
{
    /// <summary>Signed up; KYC has not been approved yet. No MT5 account exists.</summary>
    PendingKyc = 0,

    /// <summary>Admin approved KYC. MT5 provisioning has not happened yet.</summary>
    KycApproved = 1,

    /// <summary>MT5 login has been created via the Manager API. Awaiting the activation deposit.</summary>
    Provisioned = 2,

    /// <summary>Admin confirmed the $2,000 activation deposit. The account is live at Stage 1+.</summary>
    Active = 3,

    /// <summary>Compensation taken at Revival — the trader has permanently exited the program.</summary>
    Exited = 4,
}
