using MondShield.Application.Accounts;
using MondShield.Domain.Accounts;

namespace MondShield.Api.Contracts;

/// <summary>Shared account read-shape for the trader's own view and the admin lookup view.</summary>
public sealed record AccountResponse(
    Guid AccountId,
    Guid UserId,
    string Status,
    string? CurrentStage,
    long? Mt5Login,
    DateTime? ActivatedAtUtc,
    DateTime? FirstTradeAtUtc,
    DateTime CreatedAtUtc,
    BalanceCompositionResponse Composition,
    // Populated on admin views (joined from the user record); null on the trader's own /me view,
    // where the client already knows its own identity from /auth/me.
    string? Email = null,
    string? FullName = null)
{
    public static AccountResponse From(ShieldAccount account) => new(
        account.Id,
        account.UserId,
        account.Status.ToString(),
        account.CurrentStage?.ToString(),
        account.Mt5Login,
        account.ActivatedAtUtc,
        account.FirstTradeAtUtc,
        account.CreatedAtUtc,
        new BalanceCompositionResponse(
            account.Composition.InsuredCapital,
            account.Composition.Compensation,
            account.Composition.Profit,
            account.Composition.Commission,
            account.Composition.Total));

    /// <summary>Admin variant carrying the joined trader identity.</summary>
    public static AccountResponse From(AccountWithUser joined) =>
        From(joined.Account) with { Email = joined.Email, FullName = joined.FullName };
}

public sealed record BalanceCompositionResponse(
    decimal InsuredCapital,
    decimal Compensation,
    decimal Profit,
    decimal Commission,
    decimal Total);
