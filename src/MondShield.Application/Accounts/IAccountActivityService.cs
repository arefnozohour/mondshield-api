namespace MondShield.Application.Accounts;

/// <summary>
/// Builds an account's unified activity feed by merging the append-only ledger and the
/// stage-transition audit log into one chronological, newest-first list.
/// </summary>
public interface IAccountActivityService
{
    Task<IReadOnlyList<AccountActivityEntry>> GetActivityAsync(Guid accountId, CancellationToken ct = default);
}
