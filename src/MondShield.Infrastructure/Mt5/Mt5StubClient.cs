using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using MondShield.Application.Mt5;

namespace MondShield.Infrastructure.Mt5;

/// <summary>
/// In-memory fake of the MT5 Manager API — no live server required. Lets onboarding and
/// compensation-payout flows be built and exercised end-to-end before the real
/// <c>MetaQuotes.MT5ManagerAPI.dll</c> is wired in. State lives only for the process lifetime;
/// registered as a singleton so it behaves like "one MT5 server" for the whole app.
/// </summary>
public sealed class Mt5StubClient : IMt5Client
{
    private readonly ConcurrentDictionary<long, StubAccount> _accounts = new();
    private readonly string _defaultGroup;
    private long _nextLogin;

    public Mt5StubClient(IOptions<Mt5Settings> settings)
    {
        _nextLogin = settings.Value.StubStartingLogin;
        _defaultGroup = settings.Value.DefaultGroup;
    }

    public Task<Mt5AccountCreationResult> CreateAccountAsync(Mt5AccountCreationRequest request, CancellationToken ct = default)
    {
        var login = Interlocked.Increment(ref _nextLogin);

        _accounts[login] = new StubAccount(request.FullName, request.Email, _defaultGroup);

        var mainPassword = GenerateStubPassword();
        var investorPassword = GenerateStubPassword();

        return Task.FromResult(new Mt5AccountCreationResult(login, mainPassword, investorPassword));
    }

    public Task<Mt5AccountSnapshot> GetAccountSnapshotAsync(long login, CancellationToken ct = default)
    {
        var account = GetAccountOrThrow(login);

        // The stub does not simulate open positions, so equity always equals balance + credit.
        return Task.FromResult(new Mt5AccountSnapshot(login, account.Balance, account.Balance + account.Credit, account.Credit));
    }

    public Task<IReadOnlyList<Mt5Trade>> GetTradeHistoryAsync(long login, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var account = GetAccountOrThrow(login);

        IReadOnlyList<Mt5Trade> trades = account.Trades
            .Where(t => t.ClosedAtUtc >= fromUtc && t.ClosedAtUtc <= toUtc)
            .ToList();

        return Task.FromResult(trades);
    }

    public Task CreditBalanceAsync(long login, decimal amount, string comment, CancellationToken ct = default)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Credit amount cannot be negative.");
        }

        var account = GetAccountOrThrow(login);
        account.Balance += amount;

        return Task.CompletedTask;
    }

    public Task<Mt5ConnectionStatus> CheckConnectionAsync(CancellationToken ct = default) =>
        Task.FromResult(new Mt5ConnectionStatus(
            Connected: true,
            Mode: "Stub",
            Detail: $"In-memory stub — no real MT5 server. {_accounts.Count} simulated account(s)."));

    private StubAccount GetAccountOrThrow(long login) =>
        _accounts.TryGetValue(login, out var account)
            ? account
            : throw new InvalidOperationException($"No stub MT5 account exists for login {login}.");

    private static string GenerateStubPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(9));

    private sealed class StubAccount(string fullName, string email, string group)
    {
        public string FullName { get; } = fullName;
        public string Email { get; } = email;
        public string Group { get; } = group;
        public decimal Balance { get; set; }
        public decimal Credit { get; set; }
        public List<Mt5Trade> Trades { get; } = [];
    }
}
