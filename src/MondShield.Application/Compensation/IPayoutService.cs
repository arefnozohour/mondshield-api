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
}
