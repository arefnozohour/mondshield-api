namespace MondShield.Domain.Stages;

/// <summary>
/// Pure, deterministic resolver for the bidirectional stage ladder. No persistence, no time,
/// no MT5 — given a current stage (and, for up-moves, whether the profit target was met in
/// the window) it returns where the account moves and why. The single place transition rules
/// live, so an open spec question only has to be corrected here.
/// </summary>
/// <remarks>
/// UP: Revival → Rebuild → Stage1 → Stage2 → Stage3 → Vip (one step; Vip cannot advance).
/// DOWN (on compensation payout):
///   • Stage1 / Stage2 / Stage3 / Vip → drop directly to Rebuild.
///   • Rebuild → drop to Revival.
///   • Revival → EXIT the program (last chance used up).
/// </remarks>
public static class StageMachine
{
    /// <summary>
    /// Resolves an upward move. The caller decides eligibility (profit target met within
    /// <see cref="StageCatalog.LevelUpWindowDays"/> of first trade) and passes the result in;
    /// this method only applies the ladder rule.
    /// </summary>
    /// <param name="current">The account's current stage.</param>
    /// <param name="profitTargetMet">
    /// True when the stage's level-up profit target was met inside the window.
    /// </param>
    /// <returns>
    /// A transition to the next stage when eligible, otherwise a no-op result
    /// (<see cref="StageTransitionResult.Moved"/> is false).
    /// </returns>
    public static StageTransitionResult ResolveUp(StageLevel current, bool profitTargetMet)
    {
        var config = StageCatalog.For(current);

        if (!config.CanLevelUp)
        {
            return NoMove(current, TransitionDirection.Up,
                $"{current} is the top stage; no level-up.");
        }

        if (!profitTargetMet)
        {
            return NoMove(current, TransitionDirection.Up,
                "Level-up profit target not met within the window.");
        }

        var next = (StageLevel)((int)current + 1);
        return new StageTransitionResult(current, next, TransitionDirection.Up,
            Exited: false,
            Reason: $"Met the level-up profit target; advanced {current} → {next}.");
    }

    /// <summary>
    /// Resolves the downward move that follows a compensation payout. Always "moves" — every
    /// stage either drops or exits.
    /// </summary>
    /// <param name="current">The stage the compensation was paid at.</param>
    public static StageTransitionResult ResolveDownAfterCompensation(StageLevel current)
        => current switch
        {
            StageLevel.Revival => new StageTransitionResult(
                StageLevel.Revival, StageLevel.Revival, TransitionDirection.Down,
                Exited: true,
                Reason: "Compensation taken at Revival; trader exits the program."),

            StageLevel.Rebuild => new StageTransitionResult(
                StageLevel.Rebuild, StageLevel.Revival, TransitionDirection.Down,
                Exited: false,
                Reason: "Compensation taken at Rebuild; dropped to Revival."),

            // Stage1, Stage2, Stage3, Vip all drop directly to Rebuild.
            _ => new StageTransitionResult(
                current, StageLevel.Rebuild, TransitionDirection.Down,
                Exited: false,
                Reason: $"Compensation taken at {current}; dropped directly to Rebuild."),
        };

    private static StageTransitionResult NoMove(
        StageLevel current, TransitionDirection direction, string reason)
        => new(current, current, direction, Exited: false, Reason: reason);
}
