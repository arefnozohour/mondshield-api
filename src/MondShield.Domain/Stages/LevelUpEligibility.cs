namespace MondShield.Domain.Stages;

/// <summary>
/// Pure check for whether a stage's level-up profit target was met in time. Kept separate
/// from <see cref="StageMachine"/> so the ladder-transition rule and the eligibility rule can
/// each be read (and corrected) independently — <see cref="StageMachine.ResolveUp"/> takes the
/// boolean result of this check rather than recomputing it.
/// </summary>
public static class LevelUpEligibility
{
    /// <summary>
    /// True when <paramref name="stage"/> can level up at all, the elapsed days since the
    /// account's first trade are within <see cref="StageCatalog.LevelUpWindowDays"/>, and the
    /// realized profit (as a fraction of insured capital) meets the stage's target.
    /// </summary>
    /// <param name="stage">The account's current stage.</param>
    /// <param name="profitRate">
    /// Profit earned so far, expressed as a fraction of insured capital (e.g. 0.15m = 15%).
    /// </param>
    /// <param name="daysSinceFirstTrade">Calendar days elapsed since the account's first trade.</param>
    public static bool IsMet(StageLevel stage, decimal profitRate, int daysSinceFirstTrade)
    {
        var config = StageCatalog.For(stage);
        if (!config.CanLevelUp)
        {
            return false;
        }

        if (daysSinceFirstTrade > StageCatalog.LevelUpWindowDays)
        {
            return false;
        }

        return profitRate >= config.LevelUpProfitRate!.Value;
    }
}
