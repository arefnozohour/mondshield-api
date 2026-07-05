using MondShield.Application.Accounts;

namespace MondShield.Api.Contracts;

/// <summary>One row of an account's unified activity feed (ledger + stage transitions).</summary>
public sealed record ActivityEntryResponse(
    DateTime OccurredAtUtc,
    string Type,
    string Label,
    decimal? Amount)
{
    public static ActivityEntryResponse From(AccountActivityEntry entry) =>
        new(entry.OccurredAtUtc, entry.Type, entry.Label, entry.Amount);
}
