namespace MondShield.Application.Mt5;

/// <summary>
/// Port to the MT5 Manager API. Minimal surface per CLAUDE.md: provision an account into a
/// configured group, read balance/equity, read trade history &amp; commission, and credit
/// balance (used by the automated compensation payout). Behind an interface so the onboarding
/// and compensation flows can be built and exercised against <c>Mt5StubClient</c> before the
/// real <c>MetaQuotes.MT5ManagerAPI.dll</c> is wired in.
/// </summary>
public interface IMt5Client
{
    /// <summary>
    /// Provisions a new MT5 login into the configured group. Called only after KYC approval.
    /// Which group is a pure infrastructure/config concern, not a caller decision — the client
    /// implementation reads its own configured default group internally.
    /// </summary>
    Task<Mt5AccountCreationResult> CreateAccountAsync(Mt5AccountCreationRequest request, CancellationToken ct = default);

    /// <summary>Reads the current balance/equity/credit for an MT5 login.</summary>
    Task<Mt5AccountSnapshot> GetAccountSnapshotAsync(long login, CancellationToken ct = default);

    /// <summary>Reads closed trades (profit and commission) for an MT5 login within a date range.</summary>
    Task<IReadOnlyList<Mt5Trade>> GetTradeHistoryAsync(long login, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Credits the MT5 balance directly — used to pay out an approved compensation request.</summary>
    Task CreditBalanceAsync(long login, decimal amount, string comment, CancellationToken ct = default);

    /// <summary>
    /// Diagnostic: verifies the client can reach MT5 (for Live mode, forces the Manager API
    /// connection). Never throws — returns a structured pass/fail for a health-check endpoint.
    /// </summary>
    Task<Mt5ConnectionStatus> CheckConnectionAsync(CancellationToken ct = default);
}

/// <summary>Result of a connection diagnostic.</summary>
/// <param name="Connected">True if the client is usable (connected in Live mode, or the stub).</param>
/// <param name="Mode">"Stub" or "Live".</param>
/// <param name="Detail">Human-readable status or the failure reason.</param>
public sealed record Mt5ConnectionStatus(bool Connected, string Mode, string Detail);

/// <summary>Inputs for provisioning a new MT5 account.</summary>
/// <param name="FullName">Trader's full name, shown on the MT5 account.</param>
/// <param name="Email">Trader's email, shown on the MT5 account.</param>
public sealed record Mt5AccountCreationRequest(string FullName, string Email);

/// <summary>
/// The new MT5 login and its generated credentials. The trader never enters an MT5 login
/// themselves — our system provisions it and stores it against their profile.
/// </summary>
public sealed record Mt5AccountCreationResult(long Login, string MainPassword, string InvestorPassword);

/// <summary>A point-in-time read of an MT5 account's balance figures.</summary>
public sealed record Mt5AccountSnapshot(long Login, decimal Balance, decimal Equity, decimal Credit);

/// <summary>One closed trade, as needed for loss-coverage and commission-exclusion math.</summary>
/// <param name="Profit">Net trade profit/loss (positive = profit, negative = loss), excluding commission and swap.</param>
/// <param name="Commission">Commission charged on this trade — excluded from coverage by design.</param>
public sealed record Mt5Trade(long Login, DateTime ClosedAtUtc, string Symbol, decimal Profit, decimal Commission, decimal Swap);
