namespace MondShield.Domain.Ledger;

/// <summary>
/// Which composition bucket a <see cref="LedgerEntry"/> affects. Mirrors the buckets in
/// <c>MondShield.Domain.Money.BalanceComposition</c>.
/// </summary>
public enum BalanceBucket
{
    InsuredCapital = 0,
    Compensation = 1,
    Profit = 2,
    Commission = 3,
}
