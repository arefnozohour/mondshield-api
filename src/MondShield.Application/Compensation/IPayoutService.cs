using MondShield.Application.Common.Models;

namespace MondShield.Application.Compensation;

/// <summary>
/// The monthly payout job's core logic: pick up Approved compensation requests whose
/// scheduled payout date has arrived, record the ledger entry, credit MT5, update the
/// per-person cap tracker, and apply the down-stage transition. Invoked by the Hangfire
/// recurring job registered in Infrastructure — kept here as pure orchestration over
/// Application ports, with no dependency on Hangfire itself.
/// </summary>
public interface IPayoutService
{
    /// <summary>Processes every Approved request due for payout as of now. Returns how many were paid.</summary>
    Task<int> ProcessDuePayoutsAsync(CancellationToken ct = default);

    /// <summary>
    /// Reconcile a request stuck in <c>Paying</c> whose MT5 credit the admin has VERIFIED landed:
    /// completes the payout (ledger, down-transition, cap, Paid) without re-crediting MT5.
    /// </summary>
    Task<Result> ConfirmStuckPayoutAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Reconcile a request stuck in <c>Paying</c> whose MT5 credit the admin has VERIFIED did NOT land:
    /// reverts it to <c>Approved</c> so the next payout run credits it cleanly.
    /// </summary>
    Task<Result> ResetStuckPayoutAsync(Guid requestId, CancellationToken ct = default);
}
