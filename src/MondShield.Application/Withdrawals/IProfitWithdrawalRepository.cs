using MondShield.Domain.Withdrawals;

namespace MondShield.Application.Withdrawals;

/// <summary>Persistence port for <see cref="ProfitWithdrawal"/> — implemented in Infrastructure over EF Core.</summary>
public interface IProfitWithdrawalRepository
{
    Task<ProfitWithdrawal?> GetByIdAsync(Guid withdrawalId, CancellationToken ct = default);

    /// <summary>Withdrawals awaiting admin execution in MT5 — the manual-withdrawal worklist.</summary>
    Task<IReadOnlyList<ProfitWithdrawal>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>A trader's own withdrawals, newest first — backs the trader-facing history view.</summary>
    Task<IReadOnlyList<ProfitWithdrawal>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    Task AddAsync(ProfitWithdrawal withdrawal, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
