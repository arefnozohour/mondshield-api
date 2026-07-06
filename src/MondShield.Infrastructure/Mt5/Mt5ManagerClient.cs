using System.Security.Cryptography;
using MetaQuotes.MT5CommonAPI;
using MetaQuotes.MT5ManagerAPI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MondShield.Application.Mt5;

namespace MondShield.Infrastructure.Mt5;

/// <summary>
/// Real MT5 Manager API client (MetaQuotes.MT5ManagerAPI64.dll), used when <c>Mt5:Mode=Live</c>.
/// Registered as a singleton — it holds one persistent Manager connection for the whole app,
/// lazily established on first use and re-established if it drops.
///
/// The native Manager API is NOT thread-safe for concurrent calls, so every operation is
/// serialized behind <see cref="_gate"/>. Call volume here is low (onboarding provisioning and
/// the monthly payout job), so a single lock is more than adequate. The underlying calls are
/// blocking native calls; the async surface simply wraps their synchronous results.
/// </summary>
public sealed class Mt5ManagerClient : IMt5Client, IDisposable
{
    private readonly Mt5Settings _settings;
    private readonly ILogger<Mt5ManagerClient> _logger;
    private readonly object _gate = new();

    private CIMTManagerAPI? _manager;
    private bool _factoryInitialized;
    private bool _connected;
    private bool _disposed;

    public Mt5ManagerClient(IOptions<Mt5Settings> settings, ILogger<Mt5ManagerClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<Mt5AccountCreationResult> CreateAccountAsync(Mt5AccountCreationRequest request, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var manager = EnsureConnected();

            var user = manager.UserCreate()
                ?? throw new InvalidOperationException("MT5 UserCreate returned null.");

            // Login 0 tells the server to auto-assign the next login in the group's range.
            Check(user.Login(0), "UserCreate.Login");
            Check(user.Group(_settings.DefaultGroup), "UserCreate.Group");
            Check(user.Name(request.FullName), "UserCreate.Name");
            Check(user.EMail(request.Email), "UserCreate.EMail");
            Check(user.Leverage(_settings.DefaultLeverage), "UserCreate.Leverage");
            Check(user.Rights(CIMTUser.EnUsersRights.USER_RIGHT_DEFAULT), "UserCreate.Rights");

            var mainPassword = GeneratePassword();
            var investorPassword = GeneratePassword();

            Check(manager.UserAdd(user, mainPassword, investorPassword), "UserAdd");

            var login = (long)user.Login();
            _logger.LogInformation("Provisioned MT5 account {Login} in group {Group}", login, _settings.DefaultGroup);

            return Task.FromResult(new Mt5AccountCreationResult(login, mainPassword, investorPassword));
        }
    }

    public Task<Mt5AccountSnapshot> GetAccountSnapshotAsync(long login, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var manager = EnsureConnected();

            var account = manager.UserCreateAccount()
                ?? throw new InvalidOperationException("MT5 UserCreateAccount returned null.");

            Check(manager.UserAccountRequest((ulong)login, account), $"UserAccountRequest({login})");

            return Task.FromResult(new Mt5AccountSnapshot(
                login,
                (decimal)account.Balance(),
                (decimal)account.Equity(),
                (decimal)account.Credit()));
        }
    }

    public Task<IReadOnlyList<Mt5Trade>> GetTradeHistoryAsync(long login, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var manager = EnsureConnected();

            var deals = manager.DealCreateArray()
                ?? throw new InvalidOperationException("MT5 DealCreateArray returned null.");

            Check(
                manager.DealRequest((ulong)login, SMTTime.FromDateTime(fromUtc), SMTTime.FromDateTime(toUtc), deals),
                $"DealRequest({login})");

            var trades = new List<Mt5Trade>();
            var total = deals.Total();
            for (uint i = 0; i < total; i++)
            {
                var deal = deals.Next(i);
                if (deal is null)
                {
                    continue;
                }

                // Only real trades carry coverage-relevant P&L; skip balance/credit/correction
                // operations (DEAL_BALANCE etc.), which also live in the deal stream.
                var action = (CIMTDeal.EnDealAction)deal.Action();
                if (action is not (CIMTDeal.EnDealAction.DEAL_BUY or CIMTDeal.EnDealAction.DEAL_SELL))
                {
                    continue;
                }

                trades.Add(new Mt5Trade(
                    login,
                    SMTTime.ToDateTime(deal.Time()),
                    deal.Symbol(),
                    (decimal)deal.Profit(),
                    (decimal)deal.Commission(),
                    Swap: 0m));
            }

            return Task.FromResult<IReadOnlyList<Mt5Trade>>(trades);
        }
    }

    public Task CreditBalanceAsync(long login, decimal amount, string comment, CancellationToken ct = default)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Credit amount cannot be negative.");
        }

        lock (_gate)
        {
            var manager = EnsureConnected();

            // DEAL_BALANCE = a real balance deposit (tradable/withdrawable) — the correct type for
            // a compensation payout. Our own ledger records that this money is un-insured
            // compensation; MT5 just sees a balance credit.
            var res = manager.DealerBalance(
                (ulong)login,
                (double)amount,
                (uint)CIMTDeal.EnDealAction.DEAL_BALANCE,
                comment,
                out var dealId);

            // DealerBalance signals success with REQUEST_DONE, not OK.
            if (res is not (MTRetCode.MT_RET_REQUEST_DONE or MTRetCode.MT_RET_OK))
            {
                throw new InvalidOperationException($"MT5 DealerBalance({login}, {amount}) failed: {res}.");
            }

            _logger.LogInformation("Credited {Amount} to MT5 {Login} (deal {DealId}): {Comment}", amount, login, dealId, comment);
            return Task.CompletedTask;
        }
    }

    public Task<Mt5ConnectionStatus> CheckConnectionAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            try
            {
                EnsureConnected();
                return Task.FromResult(new Mt5ConnectionStatus(
                    Connected: true,
                    Mode: "Live",
                    Detail: $"Connected to {_settings.Server} as manager {_settings.ManagerLogin} " +
                            $"(API v{SMTManagerAPIFactory.ManagerAPIVersion}), group '{_settings.DefaultGroup}'."));
            }
            catch (Exception ex)
            {
                // A failed connect leaves us unconnected; report the reason instead of throwing so
                // the health endpoint returns a clean result. Next call will retry the connect.
                _connected = false;
                return Task.FromResult(new Mt5ConnectionStatus(false, "Live", ex.Message));
            }
        }
    }

    // --- connection lifecycle (always called under _gate) ---------------------------------

    private CIMTManagerAPI EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connected && _manager is not null)
        {
            return _manager;
        }

        if (string.IsNullOrWhiteSpace(_settings.Server) || _settings.ManagerLogin == 0 || string.IsNullOrEmpty(_settings.ManagerPassword))
        {
            throw new InvalidOperationException(
                "MT5 live mode is enabled but Mt5:Server / Mt5:ManagerLogin / Mt5:ManagerPassword are not configured. " +
                "Set them via user-secrets or environment variables.");
        }

        if (!_factoryInitialized)
        {
            // The managed wrapper loads the CPU-appropriate native MT5APIManager64*.dll from this
            // path — the app's own directory, where the .csproj copies them.
            var nativeDir = AppContext.BaseDirectory;
            var init = SMTManagerAPIFactory.Initialize(nativeDir);
            if (init != MTRetCode.MT_RET_OK)
            {
                throw new InvalidOperationException($"MT5 SMTManagerAPIFactory.Initialize('{nativeDir}') failed: {init}.");
            }
            _factoryInitialized = true;
        }

        var version = SMTManagerAPIFactory.ManagerAPIVersion;
        var manager = SMTManagerAPIFactory.CreateManager(version, out var createRes);
        if (createRes != MTRetCode.MT_RET_OK || manager is null)
        {
            throw new InvalidOperationException($"MT5 CreateManager failed: {createRes}.");
        }

        // PUMP_MODE_NONE: no real-time data streaming. All our operations (UserAdd,
        // UserAccountRequest, DealRequest, DealerBalance) are request/response calls that don't
        // need a pump. Any pump mode makes Connect download that data set up front — on a
        // populated broker server that's slow enough to look like a hang — so we stream nothing.
        var connect = manager.Connect(
            _settings.Server,
            (ulong)_settings.ManagerLogin,
            _settings.ManagerPassword,
            null,
            CIMTManagerAPI.EnPumpModes.PUMP_MODE_NONE,
            _settings.ConnectTimeoutMs);

        if (connect != MTRetCode.MT_RET_OK)
        {
            manager.Dispose();
            throw new InvalidOperationException(
                $"MT5 Connect to '{_settings.Server}' as manager {_settings.ManagerLogin} failed: {connect}.");
        }

        _manager = manager;
        _connected = true;
        _logger.LogInformation("Connected to MT5 Manager API at {Server} as manager {Login}", _settings.Server, _settings.ManagerLogin);
        return manager;
    }

    private static void Check(MTRetCode res, string operation)
    {
        if (res != MTRetCode.MT_RET_OK)
        {
            throw new InvalidOperationException($"MT5 {operation} failed: {res}.");
        }
    }

    /// <summary>
    /// Generates a strong password that satisfies MT5's complexity rule (upper + lower + digit):
    /// a random base64 core guarantees mixed case and digits are unlikely but not guaranteed, so
    /// we prepend a fixed compliant prefix and append a digit.
    /// </summary>
    private static string GeneratePassword() =>
        "A" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(9)).Replace("+", "x").Replace("/", "y").Replace("=", "z") + "9";

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            if (_manager is not null)
            {
                try
                {
                    if (_connected)
                    {
                        _manager.Disconnect();
                    }
                    _manager.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error while disposing MT5 manager connection");
                }
                _manager = null;
                _connected = false;
            }

            if (_factoryInitialized)
            {
                SMTManagerAPIFactory.Shutdown();
                _factoryInitialized = false;
            }
        }
    }
}
