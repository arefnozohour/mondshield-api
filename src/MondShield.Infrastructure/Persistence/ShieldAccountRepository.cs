using Microsoft.EntityFrameworkCore;
using MondShield.Application.Accounts;
using MondShield.Application.Mt5;
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

    // Tracked — the reconciliation job mutates composition/watermark on the returned entities.
    public async Task<IReadOnlyList<ShieldAccount>> GetActiveWithMt5LoginAsync(CancellationToken ct = default) =>
        await _db.ShieldAccounts
            .Where(a => a.Status == AccountStatus.Active && a.Mt5Login != null)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);

    // Read-only listing — AsNoTracking, unlike the command-flow reads above.
    public async Task<IReadOnlyList<ShieldAccount>> GetOnboardingQueueAsync(CancellationToken ct = default) =>
        await _db.ShieldAccounts.AsNoTracking()
            .Where(a => a.Status != AccountStatus.Active && a.Status != AccountStatus.Exited)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);

    public async Task<Guid?> GetActiveAccountIdByMt5LoginAsync(long mt5Login, CancellationToken ct = default) =>
        await _db.ShieldAccounts.AsNoTracking()
            .Where(a => a.Status == AccountStatus.Active && a.Mt5Login == mt5Login)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

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

    public async Task<IReadOnlySet<long>> GetKnownBalanceOpDealIdsAsync(
        Guid accountId, IReadOnlyCollection<long> dealIds, CancellationToken ct = default)
    {
        if (dealIds.Count == 0)
        {
            return new HashSet<long>();
        }

        var known = await _db.Mt5BalanceOperations.AsNoTracking()
            .Where(o => o.AccountId == accountId && dealIds.Contains(o.DealId))
            .Select(o => o.DealId)
            .ToListAsync(ct);

        return known.ToHashSet();
    }

    // Identity join so the admin worklist shows who the money moved for. AsNoTracking read-only.
    public async Task<IReadOnlyList<Mt5BalanceOperationView>> GetPendingBalanceOperationsAsync(CancellationToken ct = default) =>
        await _db.Mt5BalanceOperations.AsNoTracking()
            .Where(o => o.Status == Mt5BalanceOperationStatus.PendingReview)
            .OrderBy(o => o.OccurredAtUtc)
            .Join(_db.ShieldAccounts, o => o.AccountId, a => a.Id, (o, a) => new { o, a.UserId })
            .Join(_db.Users, x => x.UserId, u => u.Id, (x, u) => new Mt5BalanceOperationView(
                x.o.Id, x.o.AccountId, x.o.Mt5Login, x.o.DealId, x.o.Amount, x.o.Comment,
                x.o.OccurredAtUtc, x.o.ObservedAtUtc, x.o.Status.ToString(), u.Email, u.FullName))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Mt5BalanceOperation>> GetBalanceOperationsForAccountAsync(Guid accountId, CancellationToken ct = default) =>
        await _db.Mt5BalanceOperations.AsNoTracking()
            .Where(o => o.AccountId == accountId)
            .OrderByDescending(o => o.OccurredAtUtc)
            .ToListAsync(ct);

    // Tracked — the classification flow resolves the returned op and saves.
    public Task<Mt5BalanceOperation?> GetBalanceOperationByIdAsync(Guid operationId, CancellationToken ct = default) =>
        _db.Mt5BalanceOperations.FirstOrDefaultAsync(o => o.Id == operationId, ct);

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

    public Task AddBalanceOperationAsync(Mt5BalanceOperation operation, CancellationToken ct = default)
    {
        _db.Mt5BalanceOperations.Add(operation);
        return Task.CompletedTask;
    }

    public Task AddStageTransitionAsync(StageTransitionRecord record, CancellationToken ct = default)
    {
        _db.StageTransitions.Add(record);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
