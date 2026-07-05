namespace MondShield.Domain.Compensation;

/// <summary>Lifecycle of a compensation (loss) request, per CLAUDE.md's review flow.</summary>
public enum CompensationRequestStatus
{
    Submitted = 0,
    UnderReview = 1,
    Approved = 2,
    Rejected = 3,

    /// <summary>Paid by the 27th/28th Hangfire payout job; the down-transition has been applied.</summary>
    Paid = 4,
}
