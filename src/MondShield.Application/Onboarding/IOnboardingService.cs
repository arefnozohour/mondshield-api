using MondShield.Application.Common.Models;

namespace MondShield.Application.Onboarding;

/// <summary>
/// The onboarding use cases from CLAUDE.md: sign up (handled elsewhere, see
/// <c>CreateAccountForNewUserAsync</c>) → KYC review → MT5 provisioning → activation deposit.
/// Each step validates the account is in the right starting <c>AccountStatus</c> before acting.
/// </summary>
public interface IOnboardingService
{
    /// <summary>Creates the trader's ShieldAccount at <c>PendingKyc</c>. Used by startup seeding.</summary>
    Task<Result<Guid>> CreateAccountForNewUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Full self-service onboarding done at registration time: creates the trader's ShieldAccount,
    /// provisions their MT5 account via the Manager API, and immediately activates it at Stage 1
    /// with the standard $2,000 insured capital — so a freshly registered trader lands live with a
    /// $2,000 balance instead of waiting on the admin's KYC/provision/activate steps. Returns the
    /// new account id (and the one-time MT5 credentials).
    /// </summary>
    Task<Result<RegisteredTraderResult>> RegisterTraderAsync(Guid userId, string fullName, string email, CancellationToken ct = default);


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

/// <summary>
/// Result of self-service registration onboarding: the new account id, its freshly provisioned
/// MT5 login and one-time credentials, and the insured capital it was activated with.
/// </summary>
public sealed record RegisteredTraderResult(
    Guid AccountId,
    long Mt5Login,
    string MainPassword,
    string InvestorPassword,
    decimal InsuredCapital);

