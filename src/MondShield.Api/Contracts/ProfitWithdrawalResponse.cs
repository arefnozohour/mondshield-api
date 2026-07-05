using MondShield.Domain.Withdrawals;

namespace MondShield.Api.Contracts;

public sealed record ProfitWithdrawalResponse(
    Guid Id,
    Guid AccountId,
    decimal RequestedAmount,
    decimal ProfitPortion,
    decimal NonProfitPortion,
    decimal BrokerShareAmount,
    decimal NetToTrader,
    string Status,
    DateTime RequestedAtUtc,
    DateTime? CompletedAtUtc)
{
    public static ProfitWithdrawalResponse From(ProfitWithdrawal w) => new(
        w.Id,
        w.AccountId,
        w.RequestedAmount,
        w.ProfitPortion,
        w.NonProfitPortion,
        w.BrokerShareAmount,
        w.NetToTrader,
        w.Status.ToString(),
        w.RequestedAtUtc,
        w.CompletedAtUtc);
}
