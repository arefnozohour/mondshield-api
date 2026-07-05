namespace MondShield.Domain.Withdrawals;

/// <summary>
/// A profit-withdrawal worklist item. The split and broker-share figures are computed once at
/// request time (via <c>MondShield.Domain.Money.ProfitShareCalculator</c>) and shown to the
/// admin, who then executes the withdrawal manually in the MT5 Manager terminal.
/// </summary>
public class ProfitWithdrawal
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }

    /// <summary>Total amount the trader asked to withdraw.</summary>
    public decimal RequestedAmount { get; set; }

    /// <summary>The part of the withdrawal that is profit — the only part the broker shares in.</summary>
    public decimal ProfitPortion { get; set; }

    /// <summary>The remainder (returned capital and/or compensation money) — never shared.</summary>
    public decimal NonProfitPortion { get; set; }

    public decimal BrokerShareAmount { get; set; }

    public decimal NetToTrader { get; set; }

    public ProfitWithdrawalStatus Status { get; set; } = ProfitWithdrawalStatus.Requested;

    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the admin marked the manual MT5 withdrawal done.</summary>
    public DateTime? CompletedAtUtc { get; set; }
}
