namespace MondShield.Domain.Compensation;

/// <summary>
/// Running total of lifetime compensation paid to one person, against the $5,000 cap
/// (<c>MondShield.Domain.Money.MoneyConstants.LifetimeCompensationCap</c>). Keyed by user
/// (not by account) because the cap is described as per-person — it must keep applying even
/// if a trader exits and is later re-onboarded.
/// </summary>
public class CompensationCapTracker
{
    /// <summary>FK to the owning login (<c>MondShield.Domain.Identity.AppUser</c>). Also the PK.</summary>
    public Guid UserId { get; set; }

    public decimal LifetimeCompensationPaid { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
