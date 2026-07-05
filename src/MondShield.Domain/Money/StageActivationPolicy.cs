namespace MondShield.Domain.Money;

/// <summary>
/// Pure check for the re-deposit rule: activating (or re-activating after a down-transition)
/// requires a fresh <see cref="MoneyConstants.ActivationDepositAmount"/> of NEW insured
/// capital. Compensation money never counts — because <see cref="BalanceComposition"/> keeps
/// compensation and insured capital in separate buckets, this only ever needs to look at the
/// insured-capital side of a deposit.
/// </summary>
/// <remarks>
/// Worked example from the spec: receive $500 compensation in Stage 1, drop to Rebuild.
/// Depositing $1,500 to make $2,000 total (own money + compensation) is NOT valid — a fresh
/// $2,000 of insured capital is required on top of the compensation.
/// </remarks>
public static class StageActivationPolicy
{
    /// <summary>
    /// True when <paramref name="newInsuredCapitalDeposited"/> — insured capital deposited
    /// since the last activation/re-activation, NOT total balance — meets the activation
    /// threshold.
    /// </summary>
    public static bool CanActivate(decimal newInsuredCapitalDeposited)
        => newInsuredCapitalDeposited >= MoneyConstants.ActivationDepositAmount;
}
