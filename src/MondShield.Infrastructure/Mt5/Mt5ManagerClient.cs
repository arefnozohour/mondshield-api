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

    private readonly bool _realtimeEnabled;
    private readonly DealEventSink _dealSink;
    private readonly ConnectionSink _connSink;

    private CIMTManagerAPI? _manager;
    private bool _factoryInitialized;
    private bool _connected;
    private bool _disposed;

    // Set by the connection sink (pump thread) when the server drops us, read under _gate by
    // EnsureConnected to trigger a clean reconnect. Volatile: written without the gate.
    private volatile bool _disconnected;

    /// <summary>
    /// Raised for every deal the server pushes in real time (trades and balance operations alike),
    /// when real-time tracking is enabled. Fires on the native pump thread — handlers must be fast
    /// and non-throwing (the listener just enqueues the login).
    /// </summary>
    public event EventHandler<Mt5RealtimeDealEvent>? DealReceived;

    public Mt5ManagerClient(IOptions<Mt5Settings> settings, ILogger<Mt5ManagerClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        _realtimeEnabled = _settings.Realtime.Enabled;
        _dealSink = new DealEventSink(RaiseDeal);
        _connSink = new ConnectionSink(this);
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

            var addRes = manager.UserAdd(user, mainPassword, investorPassword);
            if (addRes != MTRetCode.MT_RET_OK)
            {
                // MT_RET_ERR_PARAMS on UserAdd almost always means one of the values on the user
                // object was rejected by the server — most commonly the target group doesn't exist,
                // or the leverage/name/email/password isn't allowed by it. Log the server's actual
                // group list so the correct DefaultGroup name is visible immediately.
                LogAvailableGroups(manager);
                throw new InvalidOperationException(
                    $"MT5 UserAdd failed: {addRes}. Check that group '{_settings.DefaultGroup}' exists on the " +
                    $"server (see the logged group list), that leverage {_settings.DefaultLeverage} is allowed by " +
                    "that group, and that the name/email/password are valid.");
            }


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

    public Task<IReadOnlyList<Mt5BalanceDeal>> GetBalanceOperationsAsync(long login, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var manager = EnsureConnected();

            var deals = manager.DealCreateArray()
                ?? throw new InvalidOperationException("MT5 DealCreateArray returned null.");

            Check(
                manager.DealRequest((ulong)login, SMTTime.FromDateTime(fromUtc), SMTTime.FromDateTime(toUtc), deals),
                $"DealRequest({login})");

            var ops = new List<Mt5BalanceDeal>();
            var total = deals.Total();
            for (uint i = 0; i < total; i++)
            {
                var deal = deals.Next(i);
                if (deal is null)
                {
                    continue;
                }

                // The mirror of GetTradeHistoryAsync: keep ONLY balance operations (deposits /
                // withdrawals / dealer credits), skipping the real trades handled there. For a
                // DEAL_BALANCE deal the signed amount lives in Profit() (positive = deposit,
                // negative = withdrawal). The deal ticket is the reconciliation idempotency key.
                var action = (CIMTDeal.EnDealAction)deal.Action();
                if (action != CIMTDeal.EnDealAction.DEAL_BALANCE)
                {
                    continue;
                }

                ops.Add(new Mt5BalanceDeal(
                    login,
                    (long)deal.Deal(),
                    SMTTime.ToDateTime(deal.Time()),
                    (decimal)deal.Profit(),
                    deal.Comment() ?? string.Empty));
            }

            return Task.FromResult<IReadOnlyList<Mt5BalanceDeal>>(ops);
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

        if (_connected && _manager is not null && !_disconnected)
        {
            return _manager;
        }

        // A dropped pump connection (flagged by the connection sink) needs a clean reconnect: tear
        // down the stale manager so the create/connect/subscribe below runs against a fresh one.
        if (_disconnected && _manager is not null)
        {
            try { _manager.Disconnect(); } catch { /* already down */ }
            _manager.Dispose();
            _manager = null;
            _connected = false;
            _disconnected = false;
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

        // Pump mode. Default (real-time OFF): PUMP_MODE_NONE — no streaming; every operation
        // (UserAdd, UserAccountRequest, DealRequest, DealerBalance) is a request/response call that
        // needs no pump, and any pump makes Connect download that data set up front (slow on a
        // populated server). Real-time ON: PUMP_MODE_USERS | PUMP_MODE_ACTIVITY streams user and
        // trade activity so the deal sink fires on new deals — this is a background listener
        // connection, so the heavier connect is acceptable.
        var pumpMode = _realtimeEnabled
            ? (CIMTManagerAPI.EnPumpModes)((uint)CIMTManagerAPI.EnPumpModes.PUMP_MODE_USERS
                                          | (uint)CIMTManagerAPI.EnPumpModes.PUMP_MODE_ACTIVITY)
            : CIMTManagerAPI.EnPumpModes.PUMP_MODE_NONE;

        var connect = manager.Connect(
            _settings.Server,
            (ulong)_settings.ManagerLogin,
            _settings.ManagerPassword,
            null,
            pumpMode,
            _settings.ConnectTimeoutMs);

        if (connect != MTRetCode.MT_RET_OK)
        {
            manager.Dispose();
            throw new InvalidOperationException(
                $"MT5 Connect to '{_settings.Server}' as manager {_settings.ManagerLogin} failed: {connect}.");
        }

        _manager = manager;
        _connected = true;
        _disconnected = false;
        _logger.LogInformation("Connected to MT5 Manager API at {Server} as manager {Login} (pump {Pump})",
            _settings.Server, _settings.ManagerLogin, pumpMode);

        if (_realtimeEnabled)
        {
            // Subscribe the connection sink (disconnect detection) and the deal sink (real-time deal
            // events) on this connection. Re-run on every (re)connect so a fresh manager is wired up.
            var subConn = manager.Subscribe(_connSink);
            var subDeal = manager.DealSubscribe(_dealSink);
            _logger.LogInformation("MT5 real-time sinks subscribed (manager={ConnRes}, deal={DealRes}).", subConn, subDeal);
        }

        return manager;
    }

    /// <summary>
    /// Called on the native pump thread for each new deal. Copies out primitives (the native deal is
    /// valid only for this call) and raises <see cref="DealReceived"/>. Never throws — a callback
    /// exception must not cross back into native code.
    /// </summary>
    private void RaiseDeal(CIMTDeal deal)
    {
        try
        {
            var evt = new Mt5RealtimeDealEvent((long)deal.Login(), (long)deal.Deal(), deal.Action());
            DealReceived?.Invoke(this, evt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MT5 real-time deal callback failed");
        }
    }

    private static void Check(MTRetCode res, string operation)
    {
        if (res != MTRetCode.MT_RET_OK)
        {
            throw new InvalidOperationException($"MT5 {operation} failed: {res}.");
        }
    }

    /// <summary>
    /// Logs the list of groups the server actually exposes. Used as a diagnostic when
    /// <c>UserAdd</c> fails with a params error, so the correct <c>Mt5:DefaultGroup</c> value is
    /// visible without guessing. Best-effort: never throws — a failure here must not mask the
    /// original provisioning error.
    /// </summary>
    private void LogAvailableGroups(CIMTManagerAPI manager)
    {
        try
        {
            var names = ReadGroupNames(manager);
            _logger.LogInformation(
                "MT5 server exposes {Count} group(s): {Groups}. Configured Mt5:DefaultGroup is '{DefaultGroup}'.",
                names.Count, string.Join(", ", names), _settings.DefaultGroup);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate MT5 groups for diagnostics.");
        }
    }

    /// <summary>Enumerates the group names the connected server exposes. Caller holds <c>_gate</c>.</summary>
    private static IReadOnlyList<string> ReadGroupNames(CIMTManagerAPI manager)
    {
        var group = manager.GroupCreate()
            ?? throw new InvalidOperationException("MT5 GroupCreate returned null.");

        var total = manager.GroupTotal();
        var names = new List<string>((int)total);
        for (uint i = 0; i < total; i++)
        {
            if (manager.GroupNext(i, group) == MTRetCode.MT_RET_OK)
            {
                names.Add(group.Group());
            }
        }

        return names;
    }

    public Task<IReadOnlyList<string>> ListGroupsAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            var manager = EnsureConnected();
            return Task.FromResult(ReadGroupNames(manager));
        }
    }

    /// <summary>
    /// Generates a strong password that satisfies MT5's password policy. Servers can require all
    /// four character classes — uppercase, lowercase, digit, AND a special character — and a
    /// base64 core (A–Z, a–z, 0–9 only, with +/= swapped out) guarantees none of them. A password
    /// missing a required class is rejected by UserAdd with MT_RET_USR_INVALID_PASSWORD (or, on
    /// looser servers, MT_RET_ERR_PARAMS). So we bookend the random core with a fixed compliant
    /// prefix that covers every class: an uppercase letter, a lowercase letter, a digit, and a
    /// special character.
    /// </summary>
    private static string GeneratePassword() =>
        "Aa9!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(9)).Replace("+", "x").Replace("/", "y").Replace("=", "z");


    /// <summary>
    /// Deal sink: the native pump calls <see cref="OnDealAdd"/> for every new deal (trade or balance
    /// operation). Forwards to the owner, which raises the managed event. Kept trivial so nothing
    /// heavy runs on the pump thread.
    /// </summary>
    private sealed class DealEventSink : CIMTDealSink
    {
        private readonly Action<CIMTDeal> _onAdd;
        public DealEventSink(Action<CIMTDeal> onAdd) => _onAdd = onAdd;
        public override void OnDealAdd(CIMTDeal deal) => _onAdd(deal);
    }

    /// <summary>
    /// Connection sink: flags a dropped connection so the next <see cref="EnsureConnected"/> (driven
    /// by the listener's heartbeat) reconnects cleanly instead of handing back a dead manager.
    /// </summary>
    private sealed class ConnectionSink : CIMTManagerSink
    {
        private readonly Mt5ManagerClient _owner;
        public ConnectionSink(Mt5ManagerClient owner) => _owner = owner;

        public override void OnDisconnect()
        {
            _owner._disconnected = true;
            _owner._logger.LogWarning("MT5 Manager connection dropped (OnDisconnect); will reconnect on next heartbeat.");
        }

        public override void OnConnect() =>
            _owner._logger.LogInformation("MT5 Manager connection (re)established (OnConnect).");
    }

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
