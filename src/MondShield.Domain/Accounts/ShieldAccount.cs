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

    /// <summary>When the admin confirmed the $2,000 activation deposit. Null until then.</summary>
    public DateTime? ActivatedAtUtc { get; set; }

    /// <summary>
    /// The account's first trade. Anchors the 30-day level-up window — coverage is active from
    /// this moment, with no separate waiting period.
    /// </summary>
    public DateTime? FirstTradeAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Local source-of-truth breakdown of the account's MT5 balance into insured capital,
    /// compensation, profit, and commission. Reconciled against MT5, not derived from it.
    /// </summary>
    public BalanceComposition Composition { get; set; } = BalanceComposition.Empty;
}
