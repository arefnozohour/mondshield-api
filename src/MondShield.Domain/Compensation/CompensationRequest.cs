using MondShield.Domain.Stages;

namespace MondShield.Domain.Compensation;

/// <summary>
/// A trader's loss-compensation request. The coverage figures are computed once at submission
/// time (via <c>MondShield.Domain.Money.CompensationCalculator</c>) and stored here so the
/// review/payout flow never has to recompute — and so a later change to the lifetime cap or
/// stage rates can never retroactively change an already-submitted request.
/// </summary>
/// <remarks>One request per stage per account — enforced by a unique index in persistence.</remarks>
public class CompensationRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }

    /// <summary>The stage the account was at when the request was submitted.</summary>
    public StageLevel StageAtRequest { get; set; }

    /// <summary>Total trading loss reported, before excluding commission. Positive amount.</summary>
    public decimal LossAtRequest { get; set; }

    /// <summary>Commission paid, excluded from the coverable loss. Positive amount.</summary>
    public decimal CommissionExcluded { get; set; }

    /// <summary>StageAtRequest's coverage % × (LossAtRequest − CommissionExcluded), before the cap.</summary>
    public decimal ComputedCoverage { get; set; }

    /// <summary>What is actually payable after clamping to the lifetime cap.</summary>
    public decimal CappedAmount { get; set; }

    /// <summary>True when <see cref="CappedAmount"/> was reduced by the lifetime cap.</summary>
    public bool CapReached { get; set; }

    public CompensationRequestStatus Status { get; set; } = CompensationRequestStatus.Submitted;

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Always the 27th or 28th of a Gregorian month — set once approved.</summary>
    public DateTime? ScheduledPayoutDateUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    /// <summary>Optional admin note recorded on approval/rejection.</summary>
    public string? ReviewerNote { get; set; }
}
