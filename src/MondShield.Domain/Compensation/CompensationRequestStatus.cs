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

    /// <summary>
    /// Claimed by the payout job and committed BEFORE the irreversible MT5 credit, so a crash
    /// between the credit and the final commit can never leave the request <see cref="Approved"/>
    /// (which the due-query would blindly re-credit). A request stuck here means the payout job
    /// died mid-flight: the MT5 credit may or may not have landed, so it is NOT auto-retried —
    /// it needs manual reconciliation against MT5 deal history (find the DEAL_BALANCE deal whose
    /// comment carries this request's Id).
    /// </summary>
    Paying = 5,
}
