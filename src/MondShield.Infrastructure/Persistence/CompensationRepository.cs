using Microsoft.EntityFrameworkCore;
using MondShield.Application.Compensation;
using MondShield.Domain.Compensation;
using MondShield.Domain.Stages;

namespace MondShield.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="ICompensationRepository"/>.</summary>
public sealed class CompensationRepository : ICompensationRepository
{
    private readonly MondShieldDbContext _db;

    public CompensationRepository(MondShieldDbContext db)
    {
        _db = db;
    }

    // Tracked (not AsNoTracking) — callers mutate the returned request and call
    // SaveChangesAsync, so change tracking must stay on for these command-flow reads.
    public Task<CompensationRequest?> GetRequestByIdAsync(Guid requestId, CancellationToken ct = default) =>
        _db.CompensationRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);

    public Task<bool> HasRequestForStageAsync(Guid accountId, StageLevel stage, CancellationToken ct = default) =>
        _db.CompensationRequests.AnyAsync(r => r.AccountId == accountId && r.StageAtRequest == stage, ct);

    public Task<CompensationCapTracker?> GetCapTrackerAsync(Guid userId, CancellationToken ct = default) =>
        _db.CompensationCapTrackers.FirstOrDefaultAsync(t => t.UserId == userId, ct);

    public async Task<IReadOnlyList<CompensationRequest>> GetDueForPayoutAsync(DateTime asOfUtc, CancellationToken ct = default) =>
        await _db.CompensationRequests
            .Where(r => r.Status == CompensationRequestStatus.Approved && r.ScheduledPayoutDateUtc <= asOfUtc)
            .ToListAsync(ct);

    // Read-only listings — AsNoTracking, unlike the command-flow reads above.
    public async Task<IReadOnlyList<CompensationRequest>> GetReviewQueueAsync(CancellationToken ct = default) =>
        await _db.CompensationRequests.AsNoTracking()
            .Where(r => r.Status == CompensationRequestStatus.Submitted || r.Status == CompensationRequestStatus.UnderReview)
            .OrderBy(r => r.SubmittedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CompensationCapTracker>> GetAllCapTrackersAsync(CancellationToken ct = default) =>
        await _db.CompensationCapTrackers.AsNoTracking()
            .OrderByDescending(t => t.LifetimeCompensationPaid)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CompensationRequest>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default) =>
        await _db.CompensationRequests.AsNoTracking()
            .Where(r => r.AccountId == accountId)
            .OrderByDescending(r => r.SubmittedAtUtc)
            .ToListAsync(ct);

    public Task AddRequestAsync(CompensationRequest request, CancellationToken ct = default)
    {
        _db.CompensationRequests.Add(request);
        return Task.CompletedTask;
    }

    public Task AddCapTrackerAsync(CompensationCapTracker tracker, CancellationToken ct = default)
    {
        _db.CompensationCapTrackers.Add(tracker);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
