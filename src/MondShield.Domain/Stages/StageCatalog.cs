using System.Collections.ObjectModel;

namespace MondShield.Domain.Stages;

/// <summary>
/// The single source of truth for the six stages' rule parameters, transcribed directly
/// from the broker spec table. Nothing else in the system should hardcode coverage,
/// broker-share, or level-up percentages — read them from here.
/// </summary>
/// <remarks>
/// Spec table (low → high):
/// <code>
/// Stage    Coverage  BrokerShare  LevelUp
/// Revival     20%        10%        15%
/// Rebuild     30%        20%        15%
/// Stage1      50%        30%        10%
/// Stage2      55%        25%        10%
/// Stage3      60%        20%        10%
/// Vip         30%         0%         —  (top, no level-up)
/// </code>
/// </remarks>
public static class StageCatalog
{
    /// <summary>
    /// Calendar days from the first trade within which the level-up profit target must be
    /// met to advance. Shared across every stage that can level up.
    /// </summary>
    public const int LevelUpWindowDays = 30;

    private static readonly ReadOnlyDictionary<StageLevel, StageConfig> Configs =
        new(new Dictionary<StageLevel, StageConfig>
        {
            [StageLevel.Revival] = new(StageLevel.Revival, 0.20m, 0.10m, 0.15m),
            [StageLevel.Rebuild] = new(StageLevel.Rebuild, 0.30m, 0.20m, 0.15m),
            [StageLevel.Stage1] = new(StageLevel.Stage1, 0.50m, 0.30m, 0.10m),
            [StageLevel.Stage2] = new(StageLevel.Stage2, 0.55m, 0.25m, 0.10m),
            [StageLevel.Stage3] = new(StageLevel.Stage3, 0.60m, 0.20m, 0.10m),
            [StageLevel.Vip] = new(StageLevel.Vip, 0.30m, 0.00m, null),
        });

    /// <summary>All six stage configs, ordered low → high by level.</summary>
    public static IReadOnlyCollection<StageConfig> All { get; } =
        Configs.Values.OrderBy(c => c.Level).ToList().AsReadOnly();

    /// <summary>Returns the immutable config for a stage.</summary>
    public static StageConfig For(StageLevel level) => Configs[level];
}
