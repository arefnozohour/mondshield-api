namespace MondShield.Infrastructure.Mt5;

/// <summary>
/// The minimal snapshot of a real-time MT5 deal, captured inside the pump-thread callback and handed
/// off for processing. We copy out only primitives (never the native <c>CIMTDeal</c>, which is valid
/// only for the duration of the callback). Only <see cref="Login"/> drives the work — the affected
/// account is reconciled in full — while <see cref="DealId"/> and <see cref="Action"/> are kept for
/// logging.
/// </summary>
public sealed record Mt5RealtimeDealEvent(long Login, long DealId, uint Action);
