namespace MondShield.Domain.Ledger;

/// <summary>
/// One balance change (an MT5 <c>DEAL_BALANCE</c> deal — a deposit or withdrawal) observed on an
/// account's MT5 login during reconciliation. MT5 shows only a single balance number; a trader can
/// log into the terminal and get money credited (or an admin can move money directly in the Manager
/// terminal) entirely outside our flows. Those movements used to surface only as anonymous "drift".
/// This record captures each one discretely — keyed by its unique MT5 deal ticket for idempotency —
/// so external money is no longer invisible: it is either matched to a system-originated credit or
/// queued for an admin to classify into the correct composition bucket.
/// </summary>
/// <remarks>Append-only in spirit: a row's <see cref="Status"/> is resolved once and not reopened.</remarks>
public class Mt5BalanceOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The MondShield account whose MT5 login this balance change landed on.</summary>
    public Guid AccountId { get; set; }

    /// <summary>The MT5 login the balance change occurred on.</summary>
    public long Mt5Login { get; set; }

    /// <summary>
    /// The MT5 deal ticket. Unique per login and stable across reconciliation runs — the idempotency
    /// key that stops the same deposit being recorded (and potentially booked) twice.
    /// </summary>
    public long DealId { get; set; }

    /// <summary>Signed balance change: positive = deposit/credit, negative = withdrawal/debit.</summary>
    public decimal Amount { get; set; }

    /// <summary>The MT5 deal comment — how a system-originated credit is recognized (MondShield marker).</summary>
    public string? Comment { get; set; }

    /// <summary>When the balance change occurred on MT5 (the deal time).</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>When our reconciliation first observed this deal.</summary>
    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;

    public Mt5BalanceOperationStatus Status { get; set; }

    /// <summary>The bucket an admin classified this op into. Null until <see cref="Mt5BalanceOperationStatus.Applied"/>.</summary>
    public BalanceBucket? ClassifiedBucket { get; set; }

    /// <summary>The ledger entry written when the op was classified/applied. Null otherwise.</summary>
    public Guid? LedgerEntryId { get; set; }

    /// <summary>When an admin resolved (applied/ignored) this op. Null while pending or system-recorded.</summary>
    public DateTime? ResolvedAtUtc { get; set; }

    /// <summary>Optional free-text note the admin left when resolving the op.</summary>
    public string? ResolutionNote { get; set; }
}
