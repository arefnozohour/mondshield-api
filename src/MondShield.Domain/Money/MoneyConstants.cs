namespace MondShield.Domain.Money;

/// <summary>
/// Fixed monetary parameters from the broker spec. All money is <see cref="decimal"/> —
/// never <c>double</c>/<c>float</c>. Domain math keeps full precision; rounding (if any)
/// happens at the MT5/payout boundary, not here.
/// </summary>
public static class MoneyConstants
{
    /// <summary>
    /// The deposit required to activate a stage — $2,000 (NOT $1,000, despite the public
    /// webpage). A fresh $2,000 is required again to re-activate after a down-transition;
    /// broker-paid compensation does NOT count toward it.
    /// </summary>
    public const decimal ActivationDepositAmount = 2_000m;

    /// <summary>
    /// The standalone deposit that would otherwise buy VIP directly. Reaching the top of the
    /// ladder grants VIP without it.
    /// </summary>
    public const decimal VipDirectDepositAmount = 10_000m;

    /// <summary>
    /// Maximum total compensation a single person can ever receive — modelled as a LIFETIME
    /// cap (open question: lifetime vs. per-request; lifetime for now).
    /// </summary>
    public const decimal LifetimeCompensationCap = 5_000m;
}
