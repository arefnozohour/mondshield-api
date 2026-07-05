using MondShield.Domain.Stages;

namespace MondShield.Domain.Money;

/// <summary>
/// The pure outcome of a profit-share calculation for one withdrawal. Splits the requested
/// amount into the part that is shareable profit and the part that is not (returned capital /
/// compensation), then applies the stage's broker share to the profit part only.
/// </summary>
/// <param name="Stage">Stage whose share % applied.</param>
/// <param name="BrokerShareRate">The stage broker-share fraction applied (e.g. 0.30m).</param>
/// <param name="RequestedAmount">The total amount the trader asked to withdraw.</param>
/// <param name="ProfitPortion">
/// The part of the withdrawal that is profit — the only part the broker shares in.
/// </param>
/// <param name="NonProfitPortion">
/// The remainder (returned capital and/or compensation money) — never shared.
/// </param>
/// <param name="BrokerShareAmount">BrokerShareRate × ProfitPortion.</param>
/// <param name="NetToTrader">RequestedAmount − BrokerShareAmount.</param>
public sealed record ProfitShareResult(
    StageLevel Stage,
    decimal BrokerShareRate,
    decimal RequestedAmount,
    decimal ProfitPortion,
    decimal NonProfitPortion,
    decimal BrokerShareAmount,
    decimal NetToTrader);

/// <summary>
/// Pure broker profit-share math. The broker shares ONLY in withdrawn profit — never in the
/// original insured capital and never in broker-paid compensation money. No persistence/MT5.
/// </summary>
public static class ProfitShareCalculator
{
    /// <summary>
    /// Computes the broker share on a profit withdrawal.
    /// </summary>
    /// <param name="stage">Stage the account is in — selects the broker share %.</param>
    /// <param name="requestedWithdrawal">Total amount the trader wants to withdraw. Positive.</param>
    /// <param name="availableProfit">
    /// Profit currently available to withdraw (from the local ledger's profit bucket). Only up
    /// to this much of the withdrawal is treated as shareable profit; the rest is returned
    /// capital/compensation and is not shared.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Any monetary input is negative.</exception>
    public static ProfitShareResult Calculate(
        StageLevel stage,
        decimal requestedWithdrawal,
        decimal availableProfit)
    {
        if (requestedWithdrawal < 0m)
            throw new ArgumentOutOfRangeException(nameof(requestedWithdrawal), "Withdrawal cannot be negative.");
        if (availableProfit < 0m)
            throw new ArgumentOutOfRangeException(nameof(availableProfit), "Available profit cannot be negative.");

        var shareRate = StageCatalog.For(stage).BrokerShareRate;

        // Profit is consumed first, capped at what the trader actually withdraws.
        var profitPortion = Math.Min(requestedWithdrawal, availableProfit);
        var nonProfitPortion = requestedWithdrawal - profitPortion;

        var brokerShare = profitPortion * shareRate;
        var netToTrader = requestedWithdrawal - brokerShare;

        return new ProfitShareResult(
            stage, shareRate, requestedWithdrawal, profitPortion, nonProfitPortion,
            brokerShare, netToTrader);
    }
}
