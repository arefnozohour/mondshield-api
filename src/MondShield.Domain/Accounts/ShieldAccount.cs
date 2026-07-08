using MondShield.Domain.Money;
using MondShield.Domain.Stages;

namespace MondShield.Domain.Accounts;

/// <summary>
/// The MondShield account: one per <c>AppUser</c>, created at sign-up and carried through
/// KYC, MT5 provisioning, and activation. Holds the account's place in the stage ladder and
/// the local balance-composition ledger that MT5's single balance number is reconciled
/// against.
/// </summary>
public class ShieldAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the owning login (<c>MondShield.Domain.Identity.AppUser</c>). 1:1.</summary>
    public Guid UserId { get; set; }

    public AccountStatus Status { get; set; } = AccountStatus.PendingKyc;

    /// <summary>
    /// The account's place on the stage ladder. Null until <see cref="AccountStatus.Active"/> —
    /// the ladder only applies once the account has actually started.
    /// </summary>
    public StageLevel? CurrentStage { get; set; }

    /// <summary>MT5 login created via the Manager API. Null until provisioned.</summary>
    public long? Mt5Login { get; set; }

    /// <summary>
    /// The MT5 main (trader) password from provisioning. Retained so the trader can retrieve the
    /// credentials they need to log into the MT5 terminal (they never chose the password; our
    /// system generated it). Null until provisioned.
    /// </summary>
    public string? Mt5MainPassword { get; set; }

    /// <summary>The MT5 investor (read-only) password from provisioning. Null until provisioned.</summary>
    public string? Mt5InvestorPassword { get; set; }

    /// <summary>When the admin confirmed the $2,000 activation deposit. Null until then.</summary>
    public DateTime? ActivatedAtUtc { get; set; }

    /// <summary>
    /// The account's first trade. Anchors the 30-day level-up window — coverage is active from
    /// this moment, with no separate waiting period. Set by MT5 reconciliation when the first
    /// real trade is observed.
    /// </summary>
    public DateTime? FirstTradeAtUtc { get; set; }

    /// <summary>
    /// Watermark for incremental MT5 trade-history reconciliation: the exclusive upper bound of
    /// the last synced window. The next sync reads trades closed after this instant, so realized
    /// profit and commission are each counted exactly once. Null until the first reconciliation.
    /// </summary>
    public DateTime? LastTradeSyncAtUtc { get; set; }

    /// <summary>
    /// The MT5 account balance read at the last reconciliation. Compared against the local ledger
    /// total to surface drift (e.g. capital-eroding losses this model leaves to the compensation
    /// flow). Null until the first reconciliation.
    /// </summary>
    public decimal? LastMt5Balance { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Local source-of-truth breakdown of the account's MT5 balance into insured capital,
    /// compensation, profit, and commission. Reconciled against MT5, not derived from it.
    /// </summary>
    /// <remarks>
    /// A fresh zero instance per account — NOT the shared <see cref="BalanceComposition.Empty"/>
    /// singleton: EF Core tracks this owned entity by reference, so two accounts sharing one
    /// instance makes the second insert throw ("cannot change the principal of an identifying FK").
    /// </remarks>
    public BalanceComposition Composition { get; set; } = new(0m, 0m, 0m, 0m);
}
