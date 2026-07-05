namespace MondShield.Domain.Money;

/// <summary>
/// The composition of a MondShield account's balance, split into the buckets the rules treat
/// differently. MT5 shows ONE balance number; our local ledger is the source of truth for how
/// that number breaks down. This is an immutable value object — every operation returns a NEW
/// composition, so callers can derive ledger entries from the before/after pair. Pure: no
/// persistence, no MT5, no rounding (full decimal precision is kept).
/// </summary>
/// <param name="InsuredCapital">
/// The trader's qualifying $2,000 deposit(s). Under coverage; profit-share applies to profit
/// derived from it.
/// </param>
/// <param name="Compensation">
/// Broker-paid compensation. Tradable/withdrawable but NOT insured and NOT counted toward
/// stage activation.
/// </param>
/// <param name="Profit">Trading profit. Subject to broker profit-share on withdrawal.</param>
/// <param name="Commission">Commission paid. Excluded from all coverage and share math.</param>
public sealed record BalanceComposition(
    decimal InsuredCapital,
    decimal Compensation,
    decimal Profit,
    decimal Commission)
{
    /// <summary>An empty composition (a brand-new account, nothing deposited yet).</summary>
    public static BalanceComposition Empty { get; } = new(0m, 0m, 0m, 0m);

    /// <summary>
    /// The total balance our ledger believes the account holds; should reconcile against MT5.
    /// Commission represents money already paid out as fees, so it does not add to the balance.
    /// </summary>
    public decimal Total => InsuredCapital + Compensation + Profit;

    /// <summary>
    /// Records a qualifying activation/insured deposit into the insured-capital bucket.
    /// </summary>
    public BalanceComposition AddInsuredCapital(decimal amount)
    {
        RequirePositive(amount, nameof(amount));
        return this with { InsuredCapital = InsuredCapital + amount };
    }

    /// <summary>
    /// Credits broker-paid compensation. Lands in its own bucket because it carries no coverage
    /// and never counts toward activating a stage.
    /// </summary>
    public BalanceComposition AddCompensation(decimal amount)
    {
        RequirePositive(amount, nameof(amount));
        return this with { Compensation = Compensation + amount };
    }

    /// <summary>Records realized trading profit (the share-eligible bucket).</summary>
    public BalanceComposition AddProfit(decimal amount)
    {
        RequirePositive(amount, nameof(amount));
        return this with { Profit = Profit + amount };
    }

    /// <summary>Records commission paid by the trader (excluded from coverage/share math).</summary>
    public BalanceComposition AddCommission(decimal amount)
    {
        RequirePositive(amount, nameof(amount));
        return this with { Commission = Commission + amount };
    }

    /// <summary>
    /// Applies a withdrawal, drawing from buckets in the order profit → compensation → insured
    /// capital. Profit-share is computed elsewhere (<see cref="ProfitShareCalculator"/>); this
    /// only moves money out of the composition. Capital is touched last so the capital-protection
    /// rule (withdraw only profit and the deposit stays insured) holds naturally.
    /// </summary>
    /// <param name="amount">Gross amount leaving the account. Must not exceed <see cref="Total"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Amount is negative.</exception>
    /// <exception cref="InvalidOperationException">Amount exceeds the available total.</exception>
    public BalanceComposition Withdraw(decimal amount)
    {
        RequirePositive(amount, nameof(amount));
        if (amount > Total)
            throw new InvalidOperationException("Withdrawal exceeds the available balance.");

        var remaining = amount;

        var fromProfit = Math.Min(Profit, remaining);
        remaining -= fromProfit;

        var fromCompensation = Math.Min(Compensation, remaining);
        remaining -= fromCompensation;

        var fromCapital = remaining; // guaranteed ≤ InsuredCapital by the Total check above

        return this with
        {
            Profit = Profit - fromProfit,
            Compensation = Compensation - fromCompensation,
            InsuredCapital = InsuredCapital - fromCapital,
        };
    }

    private static void RequirePositive(decimal amount, string paramName)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(paramName, "Amount cannot be negative.");
    }
}
