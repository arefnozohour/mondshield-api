namespace MondShield.Application.Accounts;

/// <summary>
/// One row in an account's unified activity feed — a merged, chronological view over the
/// append-only ledger (deposits, compensation, profit, commission, withdrawals) and the
/// stage-transition audit log (level up/down/exit). Read-only projection built for display;
/// it is not persisted.
/// </summary>
/// <param name="OccurredAtUtc">When the underlying event happened.</param>
/// <param name="Type">
/// Machine-readable kind: <c>Deposit</c>, <c>Compensation</c>, <c>Profit</c>, <c>Commission</c>,
/// <c>Withdrawal</c>, <c>StageUp</c>, <c>StageDown</c>, or <c>Exit</c>.
/// </param>
/// <param name="Label">Human-readable one-line description.</param>
/// <param name="Amount">Signed money amount for ledger rows; null for stage transitions.</param>
public sealed record AccountActivityEntry(
    DateTime OccurredAtUtc,
    string Type,
    string Label,
    decimal? Amount);
