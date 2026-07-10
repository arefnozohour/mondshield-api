using MondShield.Domain.Stages;

namespace MondShield.Application.Mt5;

/// <summary>
/// The comment convention for MT5 balance operations our own system originates. Every credit we
/// push to MT5 (currently the compensation payout) carries a comment starting with
/// <see cref="SystemPrefix"/>, so reconciliation can tell a system-originated balance deal — already
/// booked in the ledger by the originating flow — from an EXTERNAL one (a trader top-up or a manual
/// dealer op) that has never touched our ledger and must be surfaced for classification.
/// </summary>
public static class Mt5Comments
{
    /// <summary>Marker every system-originated MT5 balance-operation comment starts with.</summary>
    public const string SystemPrefix = "MondShield";

    /// <summary>The comment written for a compensation payout credit; embeds the request id for tracing.</summary>
    public static string Compensation(Guid requestId, StageLevel stage) =>
        $"{SystemPrefix} compensation {requestId} - {stage}";

    /// <summary>
    /// The comment written when our system self-funds the activation deposit into MT5 (register /
    /// admin deposit-confirmation). Embeds the account id for tracing. Carries the system marker so
    /// reconciliation records it as already-booked (the onboarding flow wrote the matching ledger
    /// entry) instead of queuing it for admin classification.
    /// </summary>
    public static string ActivationDeposit(Guid accountId) =>
        $"{SystemPrefix} activation deposit {accountId}";

    /// <summary>
    /// True if this balance-deal comment marks a credit our system originated (and therefore already
    /// booked in the ledger). False for external, un-booked money that needs admin classification.
    /// </summary>
    public static bool IsSystemOriginated(string? comment) =>
        comment is not null && comment.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase);
}
