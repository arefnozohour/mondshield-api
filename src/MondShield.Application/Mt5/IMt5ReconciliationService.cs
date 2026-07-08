using MondShield.Application.Common.Models;

namespace MondShield.Application.Mt5;

/// <summary>
/// Pulls each active account's realized trading activity from MT5 into the local ledger — the
/// missing "sync" layer. For every active account with a provisioned login it reads trade history
/// since the last watermark, records net realized profit and commission into the composition and
/// the append-only ledger, stamps <c>FirstTradeAtUtc</c> on the first observed trade, snapshots the
/// MT5 balance, and advances the watermark so nothing is double-counted. Runs on a schedule
/// (Hangfire) and can be triggered per-account on demand by an admin.
/// </summary>
public interface IMt5ReconciliationService
{
    /// <summary>Reconciles every active account with an MT5 login. Returns how many were reconciled.</summary>
    Task<int> ReconcileAllActiveAsync(CancellationToken ct = default);

    /// <summary>Reconciles a single account now (admin "sync"). Fails if the account isn't active/provisioned.</summary>
    Task<Result<Mt5ReconciliationResult>> ReconcileAccountAsync(Guid accountId, CancellationToken ct = default);
}

/// <summary>
/// Outcome of reconciling one account: how many new trades were applied, the net profit and
/// commission booked this run, the freshly read MT5 balance, our ledger total, and the drift
/// between them (MT5 balance − ledger total — non-zero drift flags losses the model leaves to the
/// compensation flow, or an out-of-band balance change).
/// </summary>
public sealed record Mt5ReconciliationResult(
    int TradesApplied,
    decimal ProfitApplied,
    decimal CommissionApplied,
    decimal Mt5Balance,
    decimal LedgerTotal,
    decimal Drift,
    DateTime SyncedAtUtc);
