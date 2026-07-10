using MondShield.Application.Mt5;

namespace MondShield.Api.Contracts;

/// <summary>
/// An MT5 balance operation (deposit/withdrawal) reconciliation captured, with the owning trader's
/// identity, for the admin classification worklist.
/// </summary>
public sealed record Mt5BalanceOperationResponse(
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
    string OwnerFullName)
{
    public static Mt5BalanceOperationResponse From(Mt5BalanceOperationView v) => new(
        v.Id, v.AccountId, v.Mt5Login, v.DealId, v.Amount, v.Comment,
        v.OccurredAtUtc, v.ObservedAtUtc, v.Status, v.OwnerEmail, v.OwnerFullName);
}

/// <summary>
/// Classify a pending external balance operation into a composition bucket. <paramref name="Bucket"/>
/// is one of InsuredCapital, Compensation, or Profit (case-insensitive).
/// </summary>
public sealed record ClassifyBalanceOperationRequest(string Bucket, string? Note);

/// <summary>Acknowledge a pending external balance operation without booking it into any bucket.</summary>
public sealed record IgnoreBalanceOperationRequest(string? Note);

/// <summary>
/// Body for the Development/Stub-only test endpoint that simulates an external MT5 balance change.
/// </summary>
public sealed record SimulateExternalBalanceOpRequest(long Login, decimal Amount, string? Comment);
