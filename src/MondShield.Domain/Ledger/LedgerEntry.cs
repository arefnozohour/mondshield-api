namespace MondShield.Domain.Ledger;

/// <summary>
/// One append-only credit/debit against an account's local balance-composition ledger. Every
/// deposit, payout, profit accrual, commission charge, and withdrawal must produce one of
/// these — it is the audit trail backing <c>ShieldAccount.Composition</c>.
/// </summary>
/// <remarks>Append-only: entries are written once and never updated or deleted.</remarks>
public class LedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }

    public BalanceBucket Bucket { get; set; }

    public LedgerEntryReason Reason { get; set; }

    /// <summary>Signed amount: positive = credited to the bucket, negative = debited from it.</summary>
    public decimal Amount { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional link to the record that caused this entry (e.g. a CompensationRequest or
    /// ProfitWithdrawal id), for tracing a ledger line back to its source.
    /// </summary>
    public Guid? RelatedRequestId { get; set; }

    public string? Note { get; set; }
}
