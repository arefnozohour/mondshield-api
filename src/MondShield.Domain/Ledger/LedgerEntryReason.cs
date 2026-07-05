namespace MondShield.Domain.Ledger;

/// <summary>What kind of event produced a <see cref="LedgerEntry"/>.</summary>
public enum LedgerEntryReason
{
    Deposit = 0,
    Compensation = 1,
    TradingProfit = 2,
    Commission = 3,
    Withdrawal = 4,
}
