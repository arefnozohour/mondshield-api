using Microsoft.Extensions.Options;
using MondShield.Application.Mt5;
using MondShield.Application.Onboarding;
using MondShield.Domain.Accounts;

namespace MondShield.Infrastructure.Mt5;

/// <summary>
/// <see cref="IMt5AccountInfoService"/> implementation: loads the ShieldAccount and pairs its
/// stored MT5 passwords with the configured server address.
/// </summary>
public sealed class Mt5AccountInfoService : IMt5AccountInfoService
{
    private readonly IShieldAccountRepository _accounts;
    private readonly Mt5Settings _settings;

    public Mt5AccountInfoService(
        IShieldAccountRepository accounts,
        IOptions<Mt5Settings> settings)
    {
        _accounts = accounts;
        _settings = settings.Value;
    }

    public async Task<Mt5AccountInfo?> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByUserIdAsync(userId, ct);
        return account is null ? null : ToInfo(account);
    }

    public async Task<Mt5AccountInfo?> GetForAccountAsync(Guid accountId, CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct);
        return account is null ? null : ToInfo(account);
    }

    private Mt5AccountInfo ToInfo(ShieldAccount account) => new(
        account.Mt5Login,
        _settings.Server,
        account.Mt5MainPassword,
        account.Mt5InvestorPassword,
        account.Mt5Login is not null);
}
