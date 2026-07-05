using MondShield.Domain.Compensation;
using MondShield.Domain.Stages;

namespace MondShield.Application.Compensation;

/// <summary>
/// Persistence port for compensation requests and the per-person cap tracker — implemented in
/// Infrastructure over EF Core. <see cref="MondShield.Domain.Accounts.ShieldAccount"/> reads
/// go through <c>MondShield.Application.Onboarding.IShieldAccountRepository</c> instead of
/// being duplicated here.
/// </summary>
public interface ICompensationRepository
{
    Task<CompensationRequest?> GetRequestByIdAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>Backs the one-request-per-stage-per-account rule with a friendly check before the DB constraint fires.</summary>
    Task<bool> HasRequestForStageAsync(Guid accountId, StageLevel stage, CancellationToken ct = default);

    /// <summary>Null if this person has never had a cap tracker row created yet (i.e. $0 paid so far).</summary>
    Task<CompensationCapTracker?> GetCapTrackerAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Approved requests whose scheduled payout date has arrived — what the payout job processes.</summary>
    Task<IReadOnlyList<CompensationRequest>> GetDueForPayoutAsync(DateTime asOfUtc, CancellationToken ct = default);

    /// <summary>Requests awaiting admin action (Submitted or UnderReview) — the admin's review queue.</summary>
    Task<IReadOnlyList<CompensationRequest>> GetReviewQueueAsync(CancellationToken ct = default);

    /// <summary>Every per-person cap tracker, for cap monitoring.</summary>
    Task<IReadOnlyList<CompensationCapTracker>> GetAllCapTrackersAsync(CancellationToken ct = default);

    /// <summary>A trader's own compensation requests, newest first — backs the trader-facing history view.</summary>
    Task<IReadOnlyList<CompensationRequest>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    Task AddRequestAsync(CompensationRequest request, CancellationToken ct = default);

    Task AddCapTrackerAsync(CompensationCapTracker tracker, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
