namespace MondShield.Domain.Stages;

/// <summary>
/// The pure outcome of resolving a stage change. Describes where the account moves and why,
/// without any persistence — callers turn this into an audit entry and apply it.
/// </summary>
/// <param name="From">The stage the account was in before the change.</param>
/// <param name="To">
/// The stage the account moves to. Equal to <paramref name="From"/> when nothing changed
/// (see <see cref="Moved"/>); irrelevant when <see cref="Exited"/> is true.
/// </param>
/// <param name="Direction">Up (profit level-up) or Down (compensation drop).</param>
/// <param name="Exited">
/// True when a down-transition removed the trader from the program entirely (a compensation
/// taken at Revival). When true, <paramref name="To"/> has no meaning.
/// </param>
/// <param name="Reason">Human-readable explanation for the audit log.</param>
public sealed record StageTransitionResult(
    StageLevel From,
    StageLevel To,
    TransitionDirection Direction,
    bool Exited,
    string Reason)
{
    /// <summary>True when the account actually changed stage or exited the program.</summary>
    public bool Moved => Exited || From != To;
}
