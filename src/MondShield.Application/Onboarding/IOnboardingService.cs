using MondShield.Application.Common.Models;

namespace MondShield.Application.Onboarding;

/// <summary>
/// The onboarding use cases from CLAUDE.md: sign up (handled elsewhere, see
/// <c>CreateAccountForNewUserAsync</c>) → KYC review → MT5 provisioning → activation deposit.
/// Each step validates the account is in the right starting <c>AccountStatus</c> before acting.
/// </summary>
public interface IOnboardingService
{
    /// <summary>Creates the trader's ShieldAccount at <c>PendingKyc</c>. Called right after registration.</summary>
    Task<Result<Guid>> CreateAccountForNewUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Admin approves KYC: <c>PendingKyc</c> → <c>KycApproved</c>.</summary>
    Task<Result> ApproveKycAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Admin triggers MT5 provisioning: <c>KycApproved</c> → <c>Provisioned</c>. Modelled as a
    /// deliberate admin action rather than automatic on KYC approval (open question in
    /// CLAUDE.md — this is the discrete step that can be switched later).
    /// </summary>
    Task<Result<Mt5ProvisioningResult>> ProvisionMt5Async(Guid accountId, string fullName, string email, CancellationToken ct = default);

    /// <summary>
    /// Admin confirms the activation deposit: <c>Provisioned</c> → <c>Active</c> at Stage 1.
    /// Fails if <paramref name="depositAmount"/> doesn't meet the $2,000 activation requirement.
    /// </summary>
    Task<Result> ActivateAsync(Guid accountId, decimal depositAmount, CancellationToken ct = default);

    /// <summary>
    /// Admin confirms the trader met their stage's level-up profit target and advances them one
    /// stage (up the ladder: Revival → Rebuild → Stage1 → Stage2 → Stage3 → VIP). This is an admin
    /// action because the app doesn't poll MT5 trade history to verify the target itself — the
    /// admin confirms it, the same way they confirm the activation deposit. Fails if the account
    /// isn't Active, or is already at the top (VIP). Returns the new stage.
    /// </summary>
    Task<Result<string>> LevelUpAsync(Guid accountId, CancellationToken ct = default);
}

/// <summary>The MT5 login and one-time credentials from provisioning, shown to the admin once.</summary>
public sealed record Mt5ProvisioningResult(long Mt5Login, string MainPassword, string InvestorPassword);
