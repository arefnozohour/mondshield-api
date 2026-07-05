using MondShield.Application.Common.Models;

namespace MondShield.Application.Compensation;

/// <summary>
/// The compensation ticket flow from CLAUDE.md: SUBMITTED → UNDER_REVIEW → APPROVED/REJECTED.
/// The actual payout (credit MT5 + down-stage transition, on the 27th/28th) is a separate
/// scheduled job — build order step 6 — that picks up Approved requests; it is not part of
/// this service.
/// </summary>
public interface ICompensationService
{
    /// <summary>
    /// Submits a loss-compensation request for the account's current stage. Computes and
    /// freezes the coverage/cap figures at submission time (see
    /// <see cref="MondShield.Domain.Compensation.CompensationRequest"/>). Fails if the account
    /// isn't Active, or a request already exists for this stage.
    /// </summary>
    Task<Result<Guid>> SubmitAsync(Guid accountId, decimal totalTradingLoss, decimal commissionPaid, CancellationToken ct = default);

    /// <summary>Admin starts reviewing: Submitted → UnderReview.</summary>
    Task<Result> StartReviewAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>Admin approves: UnderReview → Approved, and schedules the next 27th/28th payout date.</summary>
    Task<Result> ApproveAsync(Guid requestId, string? reviewerNote, CancellationToken ct = default);

    /// <summary>Admin rejects: UnderReview → Rejected.</summary>
    Task<Result> RejectAsync(Guid requestId, string? reviewerNote, CancellationToken ct = default);
}
