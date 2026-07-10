using MondShield.Application.Common.Models;
using MondShield.Domain.Ledger;

namespace MondShield.Application.Mt5;

/// <summary>
/// Admin workflow over the balance operations reconciliation captures from MT5. Reconciliation only
/// RECORDS each external balance change (as PendingReview); a human decides what it means — because
/// the rules bucket incoming money differently (a qualifying $2,000 insured deposit vs. plain
/// tradable funds vs. compensation). This service is that decision surface: list what is pending,
/// classify a deposit into a composition bucket (writing the ledger entry), or ignore an op that
/// carries no coverage (e.g. a manual withdrawal handled elsewhere).
/// </summary>
public interface IMt5BalanceOperationService
{
    /// <summary>External balance ops awaiting classification, oldest first (the admin worklist).</summary>
    Task<IReadOnlyList<Mt5BalanceOperationView>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Classify a pending external deposit into a composition bucket: book a ledger entry, credit the
    /// bucket, and mark the op Applied. Valid only for a PendingReview op with a POSITIVE amount and
    /// an incoming bucket (InsuredCapital, Compensation, or Profit) — Commission is not credited this
    /// way, and negative ops (withdrawals) must be Ignored instead.
    /// </summary>
    Task<Result> ClassifyAsync(Guid operationId, BalanceBucket bucket, string? note, CancellationToken ct = default);

    /// <summary>
    /// Acknowledge a pending external op without booking it into any bucket (e.g. a withdrawal that
    /// belongs to the profit-withdrawal flow, or funds that carry no coverage). Marks it Ignored; the
    /// drift it causes stays visible in reconciliation by design.
    /// </summary>
    Task<Result> IgnoreAsync(Guid operationId, string? note, CancellationToken ct = default);
}

/// <summary>
/// Admin-facing read model of a recorded MT5 balance operation, joined to the owning trader's
/// identity so the worklist shows who the money moved for.
/// </summary>
public sealed record Mt5BalanceOperationView(
    Guid Id,
    Guid AccountId,
    long Mt5Login,
    long DealId,
    decimal Amount,
    string? Comment,
    DateTime OccurredAtUtc,
    DateTime ObservedAtUtc,
    string Status,
    string OwnerEmail,
    string OwnerFullName);
