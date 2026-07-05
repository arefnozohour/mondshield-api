namespace MondShield.Domain.Stages;

/// <summary>
/// Immutable, persisted audit-log row for one stage change. Built from a
/// <see cref="StageTransitionResult"/> after <see cref="StageMachine"/> resolves it — the
/// pure result is computed fresh each time; this is the durable record of what actually
/// happened and when.
/// </summary>
/// <remarks>Append-only: written once, never updated or deleted.</remarks>
public class StageTransitionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }

    public StageLevel From { get; set; }

    /// <summary>The stage moved to. Meaningless when <see cref="Exited"/> is true.</summary>
    public StageLevel? To { get; set; }

    public TransitionDirection Direction { get; set; }

    /// <summary>True when a down-transition removed the trader from the program entirely.</summary>
    public bool Exited { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
