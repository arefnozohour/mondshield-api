using Microsoft.EntityFrameworkCore;
using MondShield.Application.Accounts;
using MondShield.Application.Onboarding;
using MondShield.Domain.Accounts;
using MondShield.Domain.Ledger;
using MondShield.Domain.Stages;

namespace MondShield.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IShieldAccountRepository"/>.</summary>
public sealed class ShieldAccountRepository : IShieldAccountRepository
{
    private readonly MondShieldDbContext _db;

    public ShieldAccountRepository(MondShieldDbContext db)
    {
        _db = db;
    }

    // Tracked (not AsNoTracking) — callers mutate the returned entity and call
    // SaveChangesAsync, so change tracking must stay on for these command-flow reads.
    public Task<ShieldAccount?> GetByIdAsync(Guid accountId, CancellationToken ct = default) =>
        _db.ShieldAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);

    public Task<ShieldAccount?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        _db.ShieldAccounts.FirstOrDefaultAsync(a => a.UserId == userId, ct);

    // Read-only listing — AsNoTracking, unlike the command-flow reads above.
    public async Task<IReadOnlyList<ShieldAccount>> GetOnboardingQueueAsync(CancellationToken ct = default) =>
        await _db.ShieldAccounts.AsNoTracking()
            .Where(a => a.Status != AccountStatus.Active && a.Status != AccountStatus.Exited)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);

    // Identity join: ShieldAccount ⋈ AppUser on UserId. AsNoTracking read-only projection.
    public async Task<AccountWithUser?> GetByIdWithUserAsync(Guid accountId, CancellationToken ct = default) =>
        await _db.ShieldAccounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Join(_db.Users, a => a.UserId, u => u.Id, (a, u) => new AccountWithUser(a, u.Email, u.FullName))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<AccountWithUser>> GetOnboardingQueueWithUserAsync(CancellationToken ct = default) =>
        await _db.ShieldAccounts.AsNoTracking()
            .Where(a => a.Status != AccountStatus.Active && a.Status != AccountStatus.Exited)
            .OrderBy(a => a.CreatedAtUtc)
            .Join(_db.Users, a => a.UserId, u => u.Id, (a, u) => new AccountWithUser(a, u.Email, u.FullName))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LedgerEntry>> GetLedgerEntriesAsync(Guid accountId, CancellationToken ct = default) =>
        await _db.LedgerEntries.AsNoTracking()
            .Where(e => e.AccountId == accountId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<StageTransitionRecord>> GetStageTransitionsAsync(Guid accountId, CancellationToken ct = default) =>
        await _db.StageTransitions.AsNoTracking()
            .Where(t => t.AccountId == accountId)
            .OrderByDescending(t => t.OccurredAtUtc)
            .ToListAsync(ct);

    public Task AddAsync(ShieldAccount account, CancellationToken ct = default)
    {
        _db.ShieldAccounts.Add(account);
        return Task.CompletedTask;
    }

    public Task AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default)
    {
        _db.LedgerEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task AddStageTransitionAsync(StageTransitionRecord record, CancellationToken ct = default)
    {
        _db.StageTransitions.Add(record);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
