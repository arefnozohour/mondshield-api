namespace MondShield.Domain.Compensation;

/// <summary>
/// Pure date math for the payout rule: compensation is deposited exclusively on the 27th or
/// 28th of the Gregorian month. This build fixes a single deterministic day (the 27th) so
/// approval always computes the same schedule; the Hangfire payout job (build order step 6)
/// runs on the same day.
/// </summary>
public static class PayoutSchedule
{
    public const int PayoutDayOfMonth = 27;

    /// <summary>The next 27th at or after <paramref name="fromUtc"/>.</summary>
    public static DateTime NextPayoutDateUtc(DateTime fromUtc)
    {
        var candidate = new DateTime(fromUtc.Year, fromUtc.Month, PayoutDayOfMonth, 0, 0, 0, DateTimeKind.Utc);
        return fromUtc <= candidate ? candidate : candidate.AddMonths(1);
    }
}
