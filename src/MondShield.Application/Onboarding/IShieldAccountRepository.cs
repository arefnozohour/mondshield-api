using MondShield.Application.Accounts;
using MondShield.Domain.Accounts;
using MondShield.Domain.Ledger;
using MondShield.Domain.Stages;

namespace MondShield.Application.Onboarding;

/// <summary>
/// Persistence port for <see cref="ShieldAccount"/>, its ledger, and its stage-transition audit
/// log — implemented in Infrastructure over EF Core. Bundles these writes alongside the account
/// so a single <see cref="SaveChangesAsync"/> commits them together (e.g. activation's deposit +
/// ledger entry, or a payout's ledger entry + stage drop), without introducing a heavier
/// unit-of-work abstraction.
/// </summary>
public interface IShieldAccountRepository
{
    Task<ShieldAccount?> GetByIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Null if this user has not been onboarded yet.</summary>
    Task<ShieldAccount?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Active accounts that have a provisioned MT5 login — the set the reconciliation job walks to
    /// pull trade history and balances. Tracked (the caller mutates composition/watermark and saves).
    /// </summary>
    Task<IReadOnlyList<ShieldAccount>> GetActiveWithMt5LoginAsync(CancellationToken ct = default);

    /// <summary>An account joined to its owner's identity (email + full name). Null if not found.</summary>
    Task<AccountWithUser?> GetByIdWithUserAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Accounts still in the onboarding pipeline (not yet Active or Exited) — the admin's
    /// KYC-approval / MT5-provisioning / activation-confirmation queue.
    /// </summary>
    Task<IReadOnlyList<ShieldAccount>> GetOnboardingQueueAsync(CancellationToken ct = default);

    /// <summary>The onboarding queue joined to each owner's identity for the admin worklist.</summary>
    Task<IReadOnlyList<AccountWithUser>> GetOnboardingQueueWithUserAsync(CancellationToken ct = default);

    /// <summary>Append-only ledger rows for one account, newest first. Read-only.</summary>
    Task<IReadOnlyList<LedgerEntry>> GetLedgerEntriesAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Stage-transition audit rows for one account, newest first. Read-only.</summary>
    Task<IReadOnlyList<StageTransitionRecord>> GetStageTransitionsAsync(Guid accountId, CancellationToken ct = default);

    Task AddAsync(ShieldAccount account, CancellationToken ct = default);

    Task AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default);

    Task AddStageTransitionAsync(StageTransitionRecord record, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
