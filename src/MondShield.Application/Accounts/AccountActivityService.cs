using MondShield.Application.Onboarding;
using MondShield.Domain.Ledger;
using MondShield.Domain.Stages;

namespace MondShield.Application.Accounts;

/// <summary>
/// Merges the ledger and stage-transition audit log into one chronological feed. Both sources
/// are append-only records of things that already happened, so every entry is a settled fact —
/// pending compensation/withdrawal requests live on their own screens and are deliberately not
/// duplicated here (a paid compensation already shows as a ledger credit; showing the request
/// too would double-count it).
/// </summary>
public sealed class AccountActivityService : IAccountActivityService
{
    private readonly IShieldAccountRepository _accounts;

    public AccountActivityService(IShieldAccountRepository accounts)
    {
        _accounts = accounts;
    }

    public async Task<IReadOnlyList<AccountActivityEntry>> GetActivityAsync(Guid accountId, CancellationToken ct = default)
    {
        var ledger = await _accounts.GetLedgerEntriesAsync(accountId, ct);
        var transitions = await _accounts.GetStageTransitionsAsync(accountId, ct);

        var entries = new List<AccountActivityEntry>(ledger.Count + transitions.Count);

        foreach (var e in ledger)
        {
            entries.Add(new AccountActivityEntry(
                e.OccurredAtUtc,
                LedgerType(e.Reason),
                LedgerLabel(e),
                e.Amount));
        }

        foreach (var t in transitions)
        {
            entries.Add(new AccountActivityEntry(
                t.OccurredAtUtc,
                t.Exited ? "Exit" : t.Direction == TransitionDirection.Up ? "StageUp" : "StageDown",
                TransitionLabel(t),
                Amount: null));
        }

        return entries
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToList();
    }

    private static string LedgerType(LedgerEntryReason reason) => reason switch
    {
        LedgerEntryReason.Deposit => "Deposit",
        LedgerEntryReason.Compensation => "Compensation",
        LedgerEntryReason.TradingProfit => "Profit",
        LedgerEntryReason.Commission => "Commission",
        LedgerEntryReason.Withdrawal => "Withdrawal",
        _ => "Ledger",
    };

    private static string LedgerLabel(LedgerEntry e) => e.Note is { Length: > 0 } note
        ? note
        : e.Reason switch
        {
            LedgerEntryReason.Deposit => "Deposit to insured capital",
            LedgerEntryReason.Compensation => "Compensation credited",
            LedgerEntryReason.TradingProfit => "Trading profit accrued",
            LedgerEntryReason.Commission => "Commission charged",
            LedgerEntryReason.Withdrawal => "Profit withdrawal",
            _ => "Ledger entry",
        };

    private static string TransitionLabel(StageTransitionRecord t)
    {
        if (t.Exited)
        {
            return $"Exited the program from {t.From}";
        }

        var to = t.To?.ToString() ?? "—";
        return t.Direction == TransitionDirection.Up
            ? $"Advanced from {t.From} to {to}"
            : $"Dropped from {t.From} to {to}";
    }
}
