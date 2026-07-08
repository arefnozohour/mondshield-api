using MondShield.Application.Mt5;

namespace MondShield.Api.Contracts;

/// <summary>
/// A trader's provisioned MT5 account credentials — the login, the server to connect to, and the
/// generated main + investor passwords. Sensitive: only returned to the account's own owner and to
/// admins. <see cref="Mt5Login"/> is null and the passwords empty until the account is provisioned.
/// </summary>
public sealed record Mt5AccountInfoResponse(
    long? Mt5Login,
    string Server,
    string? MainPassword,
    string? InvestorPassword,
    bool IsProvisioned)
{
    public static Mt5AccountInfoResponse From(Mt5AccountInfo info) => new(
        info.Login,
        info.Server,
        info.MainPassword,
        info.InvestorPassword,
        info.IsProvisioned);
}
