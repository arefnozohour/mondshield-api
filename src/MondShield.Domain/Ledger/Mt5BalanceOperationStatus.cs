namespace MondShield.Domain.Ledger;

/// <summary>
/// Lifecycle of an <see cref="Mt5BalanceOperation"/> — a balance change (deposit/withdrawal)
/// observed on the MT5 side and reconciled against our local ledger.
/// </summary>
public enum Mt5BalanceOperationStatus
{
    /// <summary>
    /// An external balance change we did NOT originate (a trader top-up, a dealer credit, a manual
    /// withdrawal). Not yet reflected in the composition — awaiting an admin classification, because
    /// the rules bucket incoming money differently (insured $2,000 vs. plain tradable funds). This is
    /// the case the local ledger was previously blind to.
    /// </summary>
    PendingReview = 0,

    /// <summary>
    /// A balance change our own system originated (its comment carries the MondShield marker — e.g.
    /// a compensation payout). The originating flow already wrote the ledger entry, so this row is
    /// recorded for audit/matching only and must NOT be booked again.
    /// </summary>
    RecordedFromSystem = 1,

    /// <summary>An admin classified a pending external op into a bucket; a ledger entry was written.</summary>
    Applied = 2,

    /// <summary>
    /// An admin acknowledged a pending external op but chose not to book it into any bucket (e.g. a
    /// manual withdrawal that belongs to the profit-withdrawal flow, or funds that carry no coverage).
    /// The drift it causes stays visible in reconciliation by design.
    /// </summary>
    Ignored = 3,
}
