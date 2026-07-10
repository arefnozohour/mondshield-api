using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MondShield.Application.Mt5;
using MondShield.Application.Onboarding;

namespace MondShield.Infrastructure.Mt5;

/// <summary>
/// Real-time MT5 tracking. Keeps the pumped Manager connection alive and, whenever the server pushes
/// a deal (a trade closing or money moving), triggers an immediate reconciliation of the affected
/// account — the same idempotent <see cref="IMt5ReconciliationService.ReconcileAccountAsync"/> the
/// hourly job runs, so this only cuts latency (seconds instead of up to an hour) and never
/// double-books. Registered as a hosted service only in Live mode with real-time enabled.
/// </summary>
public sealed class Mt5RealtimeListener : BackgroundService
{
    private readonly Mt5ManagerClient _client;
    private readonly IServiceScopeFactory _scopes;
    private readonly Mt5RealtimeSettings _settings;
    private readonly ILogger<Mt5RealtimeListener> _logger;

    // Logins pushed from the pump-thread callback; drained and coalesced by the processing loop.
    private readonly Channel<long> _logins =
        Channel.CreateUnbounded<long>(new UnboundedChannelOptions { SingleReader = true });

    public Mt5RealtimeListener(
        Mt5ManagerClient client,
        IServiceScopeFactory scopes,
        IOptions<Mt5Settings> settings,
        ILogger<Mt5RealtimeListener> logger)
    {
        _client = client;
        _scopes = scopes;
        _settings = settings.Value.Realtime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MT5 real-time listener starting (debounce {Debounce}ms, heartbeat {Heartbeat}s).",
            _settings.DebounceMs, _settings.HeartbeatSeconds);

        _client.DealReceived += OnDealReceived;
        try
        {
            // Heartbeat forces the initial (and any post-drop) connection + sink subscription; the
            // processing loop drains coalesced logins and reconciles. Both honour the stop token.
            await Task.WhenAll(HeartbeatLoopAsync(stoppingToken), ProcessLoopAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
        finally
        {
            _client.DealReceived -= OnDealReceived;
            _logger.LogInformation("MT5 real-time listener stopped.");
        }
    }

    // Pump-thread callback: enqueue the login and return immediately. TryWrite never blocks on an
    // unbounded channel.
    private void OnDealReceived(object? sender, Mt5RealtimeDealEvent e) => _logins.Writer.TryWrite(e.Login);

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.HeartbeatSeconds));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Forces EnsureConnected: establishes the pumped connection on first run and
                // reconnects after a drop (the connection sink flags one).
                var status = await _client.CheckConnectionAsync(ct);
                if (!status.Connected)
                {
                    _logger.LogWarning("MT5 real-time: connection not established: {Detail}", status.Detail);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MT5 real-time heartbeat failed");
            }

            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        var reader = _logins.Reader;
        var debounce = TimeSpan.FromMilliseconds(Math.Max(0, _settings.DebounceMs));

        while (await reader.WaitToReadAsync(ct))
        {
            var batch = new HashSet<long>();
            while (reader.TryRead(out var login))
            {
                batch.Add(login);
            }

            // Let a burst of deals from one position close settle, then sweep up the rest, so the
            // affected account is reconciled once rather than per deal.
            if (debounce > TimeSpan.Zero)
            {
                try { await Task.Delay(debounce, ct); }
                catch (OperationCanceledException) { break; }
                while (reader.TryRead(out var login))
                {
                    batch.Add(login);
                }
            }

            foreach (var login in batch)
            {
                await ReconcileLoginAsync(login, ct);
            }
        }
    }

    private async Task ReconcileLoginAsync(long login, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IShieldAccountRepository>();

            var accountId = await accounts.GetActiveAccountIdByMt5LoginAsync(login, ct);
            if (accountId is null)
            {
                _logger.LogDebug("MT5 real-time: deal on login {Login} has no active tracked account; ignored.", login);
                return;
            }

            var reconciliation = scope.ServiceProvider.GetRequiredService<IMt5ReconciliationService>();
            var result = await reconciliation.ReconcileAccountAsync(accountId.Value, ct);

            if (result.Succeeded)
            {
                var r = result.Value!;
                _logger.LogInformation(
                    "MT5 real-time reconcile login {Login}: trades={Trades}, balanceOps={Ops} (pending {Pending}), drift={Drift}.",
                    login, r.TradesApplied, r.BalanceOpsObserved, r.BalanceOpsPendingReview, r.Drift);
            }
            else
            {
                _logger.LogWarning("MT5 real-time reconcile login {Login} failed: {Errors}", login, string.Join("; ", result.Errors));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MT5 real-time reconcile for login {Login} threw", login);
        }
    }
}
