using MondShield.Application.Accounts;
using MondShield.Application.Mt5;
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

    /// <summary>
    /// The id of the ACTIVE account provisioned with this MT5 login, or null if none. Used by the
    /// real-time listener to map a pushed deal (which carries only the MT5 login) to the account to
    /// reconcile.
    /// </summary>
    Task<Guid?> GetActiveAccountIdByMt5LoginAsync(long mt5Login, CancellationToken ct = default);

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

    /// <summary>
    /// The MT5 deal tickets, out of <paramref name="dealIds"/>, this account has already recorded a
    /// balance operation for. Reconciliation uses it to skip deals it has seen before — deduping by
    /// the immutable MT5 ticket so a deposit is never captured (or booked) twice across runs.
    /// </summary>
    Task<IReadOnlySet<long>> GetKnownBalanceOpDealIdsAsync(Guid accountId, IReadOnlyCollection<long> dealIds, CancellationToken ct = default);

    /// <summary>Balance operations awaiting admin classification (PendingReview), joined to the owner's identity.</summary>
    Task<IReadOnlyList<Mt5BalanceOperationView>> GetPendingBalanceOperationsAsync(CancellationToken ct = default);

    /// <summary>All balance operations recorded for one account, newest first. Read-only.</summary>
    Task<IReadOnlyList<Mt5BalanceOperation>> GetBalanceOperationsForAccountAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>A single balance operation by id, tracked — the caller resolves it and saves.</summary>
    Task<Mt5BalanceOperation?> GetBalanceOperationByIdAsync(Guid operationId, CancellationToken ct = default);

    Task AddAsync(ShieldAccount account, CancellationToken ct = default);

    Task AddLedgerEntryAsync(LedgerEntry entry, CancellationToken ct = default);

    Task AddBalanceOperationAsync(Mt5BalanceOperation operation, CancellationToken ct = default);

    Task AddStageTransitionAsync(StageTransitionRecord record, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
