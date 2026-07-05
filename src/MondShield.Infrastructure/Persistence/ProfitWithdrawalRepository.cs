using Microsoft.EntityFrameworkCore;
using MondShield.Application.Withdrawals;
using MondShield.Domain.Withdrawals;

namespace MondShield.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IProfitWithdrawalRepository"/>.</summary>
public sealed class ProfitWithdrawalRepository : IProfitWithdrawalRepository
{
    private readonly MondShieldDbContext _db;

    public ProfitWithdrawalRepository(MondShieldDbContext db)
    {
        _db = db;
    }

    // Tracked (not AsNoTracking) — callers mutate the returned entity and call
    // SaveChangesAsync, so change tracking must stay on for these command-flow reads.
    public Task<ProfitWithdrawal?> GetByIdAsync(Guid withdrawalId, CancellationToken ct = default) =>
        _db.ProfitWithdrawals.FirstOrDefaultAsync(w => w.Id == withdrawalId, ct);

    // Read-only listing — AsNoTracking, unlike the command-flow read above.
    public async Task<IReadOnlyList<ProfitWithdrawal>> GetPendingAsync(CancellationToken ct = default) =>
        await _db.ProfitWithdrawals.AsNoTracking()
            .Where(w => w.Status == ProfitWithdrawalStatus.Requested)
            .OrderBy(w => w.RequestedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProfitWithdrawal>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default) =>
        await _db.ProfitWithdrawals.AsNoTracking()
            .Where(w => w.AccountId == accountId)
            .OrderByDescending(w => w.RequestedAtUtc)
            .ToListAsync(ct);

    public Task AddAsync(ProfitWithdrawal withdrawal, CancellationToken ct = default)
    {
        _db.ProfitWithdrawals.Add(withdrawal);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
