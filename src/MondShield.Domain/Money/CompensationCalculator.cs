using MondShield.Domain.Stages;

namespace MondShield.Domain.Money;

/// <summary>
/// The pure outcome of a loss-compensation calculation. Carries both the raw computed
/// coverage and the figure actually payable after the lifetime cap, so the ledger and the
/// audit log can record exactly how much of the cap was used.
/// </summary>
/// <param name="Stage">The stage the compensation is requested at.</param>
/// <param name="CoverageRate">The stage coverage fraction applied (e.g. 0.50m).</param>
/// <param name="CoverableLoss">Total trading loss minus excluded commission.</param>
/// <param name="ComputedCoverage">CoverageRate × CoverableLoss, before the cap.</param>
/// <param name="PayableAmount">
/// What is actually paid out — <see cref="ComputedCoverage"/> reduced so the person's
/// lifetime total never exceeds <see cref="MoneyConstants.LifetimeCompensationCap"/>.
/// </param>
/// <param name="CapReached">True when the cap clipped the payout (or was already exhausted).</param>
public sealed record CompensationResult(
    StageLevel Stage,
    decimal CoverageRate,
    decimal CoverableLoss,
    decimal ComputedCoverage,
    decimal PayableAmount,
    bool CapReached);

/// <summary>
/// Pure loss-coverage math. No persistence, no MT5, no time. Coverage = stage% × qualifying
/// loss (commission excluded), then clamped to the per-person lifetime cap.
/// </summary>
public static class CompensationCalculator
{
    /// <summary>
    /// Computes the compensation payable for a loss at a given stage, honouring the lifetime cap.
    /// </summary>
    /// <param name="stage">Stage the request is made at — selects the coverage %.</param>
    /// <param name="totalTradingLoss">
    /// Total trading loss as a positive amount (e.g. a $300 loss is <c>300m</c>).
    /// </param>
    /// <param name="commissionPaid">
    /// Commission the trader paid, excluded from coverage by design. Positive amount.
    /// </param>
    /// <param name="lifetimeCompensationAlreadyPaid">
    /// Sum of all compensation this person has already received, for the cap check.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Any monetary input is negative.</exception>
    public static CompensationResult Calculate(
        StageLevel stage,
        decimal totalTradingLoss,
        decimal commissionPaid,
        decimal lifetimeCompensationAlreadyPaid)
    {
        if (totalTradingLoss < 0m)
            throw new ArgumentOutOfRangeException(nameof(totalTradingLoss), "Loss cannot be negative.");
        if (commissionPaid < 0m)
            throw new ArgumentOutOfRangeException(nameof(commissionPaid), "Commission cannot be negative.");
        if (lifetimeCompensationAlreadyPaid < 0m)
            throw new ArgumentOutOfRangeException(nameof(lifetimeCompensationAlreadyPaid),
                "Lifetime compensation already paid cannot be negative.");

        var coverageRate = StageCatalog.For(stage).CoverageRate;

        // Commission is excluded from coverage; never let it push the coverable loss below zero.
        var coverableLoss = Math.Max(0m, totalTradingLoss - commissionPaid);
        var computedCoverage = coverableLoss * coverageRate;

        // Clamp to whatever lifetime cap headroom remains (never negative).
        var remainingCap = Math.Max(0m, MoneyConstants.LifetimeCompensationCap - lifetimeCompensationAlreadyPaid);
        var payable = Math.Min(computedCoverage, remainingCap);
        var capReached = payable < computedCoverage;

        return new CompensationResult(
            stage, coverageRate, coverableLoss, computedCoverage, payable, capReached);
    }
}
