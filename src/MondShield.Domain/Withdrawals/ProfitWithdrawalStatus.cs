namespace MondShield.Domain.Withdrawals;

/// <summary>
/// Withdrawals are manual: a human executes them in the MT5 Manager terminal after the
/// profit-share calc is shown, then marks the record done. No automation in this build.
/// </summary>
public enum ProfitWithdrawalStatus
{
    Requested = 0,
    Completed = 1,
}
