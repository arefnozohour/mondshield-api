using Microsoft.Extensions.Logging;
using MondShield.Application.Common.Models;
using MondShield.Application.Onboarding;
using MondShield.Domain.Accounts;
using MondShield.Domain.Ledger;

namespace MondShield.Application.Mt5;

public sealed class Mt5ReconciliationService : IMt5ReconciliationService
{
    // Log a warning when |drift| exceeds this, so an out-of-band balance change or capital-eroding
    // loss doesn't pass silently. Small enough to catch real gaps, above cent-level rounding noise.
    private const decimal DriftWarnThreshold = 0.01m;

    private readonly IShieldAccountRepository _accounts;
    private readonly IMt5Client _mt5;
    private readonly ILogger<Mt5ReconciliationService> _logger;

    public Mt5ReconciliationService(
        IShieldAccountRepository accounts,
        IMt5Client mt5,
        ILogger<Mt5ReconciliationService> logger)
    {
        _accounts = accounts;
        _mt5 = mt5;
        _logger = logger;
    }

    public async Task<int> ReconcileAllActiveAsync(CancellationToken ct = default)
    {
        var accounts = await _accounts.GetActiveWithMt5LoginAsync(ct);
        var reconciled = 0;

        foreach (var account in accounts)
        {
            try
            {
                // The MT5 reads happen first; if either throws, the account is left untouched and
                // skipped. The in-memory mutations that follow can't throw, so a failure never
                // leaves an account half-updated for the single commit below.
                await ReconcileOneAsync(account, ct);
                reconciled++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MT5 reconciliation failed for account {AccountId} (login {Login})", account.Id, account.Mt5Login);
            }
        }

        // One commit for the whole batch — every ledger entry, composition update, and watermark
        // advance across the reconciled accounts lands atomically. Skipped accounts contributed no
        // changes, so they aren't affected.
        if (reconciled > 0)
        {
            await _accounts.SaveChangesAsync(ct);
        }

        _logger.LogInformation("MT5 reconciliation reconciled {Count}/{Total} active account(s).", reconciled, accounts.Count);
        return reconciled;
    }

    public async Task<Result<Mt5ReconciliationResult>> ReconcileAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        if (account is null)
        {
            return Result<Mt5ReconciliationResult>.Failure("Account not found.");
        }

        if (account.Status != AccountStatus.Active || account.Mt5Login is null)
        {
            return Result<Mt5ReconciliationResult>.Failure("Account must be active with a provisioned MT5 login to reconcile.");
        }

        Mt5ReconciliationResult result;
        try
        {
            result = await ReconcileOneAsync(account, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MT5 reconciliation failed for account {AccountId} (login {Login})", account.Id, account.Mt5Login);
            return Result<Mt5ReconciliationResult>.Failure($"MT5 reconciliation failed: {ex.Message}");
        }

        await _accounts.SaveChangesAsync(ct);
        return Result<Mt5ReconciliationResult>.Success(result);
    }

    /// <summary>
    /// Reconciles one tracked account: reads MT5, books the deltas into the composition and ledger,
    /// and advances the watermark. Does NOT save — the caller owns the commit (one per account for
    /// the on-demand path, one for the whole batch on the scheduled path).
    /// </summary>
    private async Task<Mt5ReconciliationResult> ReconcileOneAsync(ShieldAccount account, CancellationToken ct)
    {
        var login = account.Mt5Login!.Value;

        // Half-open window (from, syncUpTo]: start where the last sync ended (or account activation
        // on the first run), end at "now" captured once. Trades are filtered strictly after `from`
        // so the boundary trade counted last run is never counted again.
        var from = account.LastTradeSyncAtUtc ?? account.ActivatedAtUtc ?? account.CreatedAtUtc;
        var syncUpTo = DateTime.UtcNow;

        var trades = await _mt5.GetTradeHistoryAsync(login, from, syncUpTo, ct);
        var snapshot = await _mt5.GetAccountSnapshotAsync(login, ct);

        var newTrades = trades
            .Where(t => t.ClosedAtUtc > from && t.ClosedAtUtc <= syncUpTo)
            .ToList();

        var grossProfit = newTrades.Sum(t => t.Profit);
        var commission = newTrades.Sum(t => t.Commission);

        // Net cash effect of trading on the MT5 balance is profit minus commission, so booking the
        // NET into the profit bucket keeps our ledger total reconciling with the MT5 balance. The
        // commission is ALSO tracked in its own bucket for the coverage-exclusion rule (it never
        // counts toward the balance total — see BalanceComposition.Total).
        var netProfit = grossProfit - commission;

        if (account.FirstTradeAtUtc is null && newTrades.Count > 0)
        {
            account.FirstTradeAtUtc = newTrades.Min(t => t.ClosedAtUtc);
        }

        var before = account.Composition;
        var after = before.ApplyRealizedProfit(netProfit);
        if (commission > 0m)
        {
            after = after.AddCommission(commission);
        }
        account.Composition = after;

        var profitDelta = after.Profit - before.Profit;
        if (profitDelta != 0m)
        {
            await _accounts.AddLedgerEntryAsync(new LedgerEntry
            {
                AccountId = account.Id,
                Bucket = BalanceBucket.Profit,
                Reason = LedgerEntryReason.TradingProfit,
                Amount = profitDelta,
                Note = $"MT5 sync: net realized P&L from {newTrades.Count} trade(s)",
            }, ct);
        }

        if (commission > 0m)
        {
            await _accounts.AddLedgerEntryAsync(new LedgerEntry
            {
                AccountId = account.Id,
                Bucket = BalanceBucket.Commission,
                Reason = LedgerEntryReason.Commission,
                Amount = commission,
                Note = "MT5 sync: commission",
            }, ct);
        }

        account.LastTradeSyncAtUtc = syncUpTo;
        account.LastMt5Balance = snapshot.Balance;

        var ledgerTotal = after.Total;
        var drift = snapshot.Balance - ledgerTotal;

        if (Math.Abs(drift) > DriftWarnThreshold)
        {
            // Expected causes: capital-eroding losses this model leaves to the compensation flow,
            // or a balance change made directly in MT5 (manual dealer op) that never went through us.
            _logger.LogWarning(
                "MT5 reconciliation drift for account {AccountId} (login {Login}): MT5 balance {Balance} vs ledger total {Total} (drift {Drift}).",
                account.Id, login, snapshot.Balance, ledgerTotal, drift);
        }

        return new Mt5ReconciliationResult(
            newTrades.Count, profitDelta, commission, snapshot.Balance, ledgerTotal, drift, syncUpTo);
    }
}
