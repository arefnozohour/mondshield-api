namespace MondShield.Application.Mt5;

/// <summary>
/// Read-side query for a trader's provisioned MT5 account credentials — the login, the server to
/// connect to, and the (decrypted) main + investor passwords. Kept separate from the general
/// account read-shape because these are sensitive: only the account's own owner and an admin can
/// fetch them, and the passwords are decrypted on demand rather than ever leaving the DB in the
/// balance/status views. Implemented in Infrastructure (needs the credential protector and the
/// configured MT5 server address).
/// </summary>
public interface IMt5AccountInfoService
{
    /// <summary>The MT5 account info for the given user's ShieldAccount, or null if they have no account.</summary>
    Task<Mt5AccountInfo?> GetForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The MT5 account info for a specific account (admin lookup), or null if not found.</summary>
    Task<Mt5AccountInfo?> GetForAccountAsync(Guid accountId, CancellationToken ct = default);
}

/// <summary>
/// The MT5 login and connection credentials for one account. <paramref name="Login"/> is null and
/// the passwords empty until the account has been provisioned. <paramref name="Server"/> is the
/// configured MT5 server address (empty in stub mode, where no real server exists).
/// </summary>
public sealed record Mt5AccountInfo(
    long? Login,
    string Server,
    string? MainPassword,
    string? InvestorPassword,
    bool IsProvisioned);
