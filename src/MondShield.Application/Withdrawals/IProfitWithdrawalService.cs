using MondShield.Application.Common.Models;

namespace MondShield.Application.Withdrawals;

/// <summary>
/// The manual profit-withdrawal worklist from CLAUDE.md: we compute the profit-share split
/// and create a flagged record; a human executes the withdrawal in the MT5 Manager terminal,
/// then marks it done. No withdrawal automation in this build.
/// </summary>
public interface IProfitWithdrawalService
{
    /// <summary>
    /// Computes and freezes the profit-share split for a withdrawal request — this is the
    /// figure "shown" to the admin before they execute it in MT5. Does not move any money yet.
    /// </summary>
    Task<Result<Guid>> RequestAsync(Guid accountId, decimal requestedAmount, CancellationToken ct = default);

    /// <summary>
    /// Admin confirms the withdrawal was executed in MT5: debits the local ledger (profit →
    /// compensation → insured capital, matching <c>BalanceComposition.Withdraw</c>'s order)
    /// against the account's current balance, and marks the record Completed.
    /// </summary>
    Task<Result> CompleteAsync(Guid withdrawalId, CancellationToken ct = default);
}
