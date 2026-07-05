namespace MondShield.Domain.Stages;

/// <summary>Direction of a stage change in the ledger / audit log.</summary>
public enum TransitionDirection
{
    /// <summary>Advanced a level by meeting the profit target in time.</summary>
    Up = 1,

    /// <summary>Dropped a level (or exited) after a compensation payout.</summary>
    Down = 2,
}
