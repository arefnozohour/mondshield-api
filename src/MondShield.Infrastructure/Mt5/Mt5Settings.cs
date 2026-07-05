namespace MondShield.Infrastructure.Mt5;

/// <summary>Strongly-typed MT5 configuration, bound from the "Mt5" config section.</summary>
public sealed class Mt5Settings
{
    public const string SectionName = "Mt5";

    /// <summary>
    /// The MT5 group newly provisioned MondShield accounts are created into. Sets leverage,
    /// permissions, and symbol set — configuration, never hardcoded.
    /// </summary>
    public string DefaultGroup { get; set; } = "demo\\mondshield";

    /// <summary>Starting login number handed out by <c>Mt5StubClient</c>'s in-memory counter.</summary>
    public long StubStartingLogin { get; set; } = 1_000_000;

    // --- Live MT5 Manager API connection ---------------------------------------------------
    // Consumed by the REAL client (MetaQuotes.MT5ManagerAPI.dll) once it replaces Mt5StubClient
    // (CLAUDE.md build-order step 10). The stub ignores all three. Supply these per-environment
    // via user-secrets or environment variables — NEVER commit real values (especially the
    // manager password), same rule as the DB connection string and JWT signing key.

    /// <summary>MT5 Manager server address, e.g. "203.0.113.10:443" (IP or host, with port).</summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>Manager API login (a manager account number with API access on the server).</summary>
    public long ManagerLogin { get; set; }

    /// <summary>Manager API password. Secret — set via user-secrets / env, never committed.</summary>
    public string ManagerPassword { get; set; } = string.Empty;

    /// <summary>Leverage assigned to newly provisioned accounts (the group also constrains this).</summary>
    public uint DefaultLeverage { get; set; } = 100;

    /// <summary>Connect timeout in milliseconds for the live Manager API connection.</summary>
    public uint ConnectTimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// Which client backing to use. <c>Stub</c> = the in-memory fake (default, no server needed);
    /// <c>Live</c> = the real MetaQuotes Manager API against <see cref="Server"/>.
    /// </summary>
    public Mt5Mode Mode { get; set; } = Mt5Mode.Stub;
}

/// <summary>Selects the <c>IMt5Client</c> implementation at composition time.</summary>
public enum Mt5Mode
{
    Stub = 0,
    Live = 1,
}
