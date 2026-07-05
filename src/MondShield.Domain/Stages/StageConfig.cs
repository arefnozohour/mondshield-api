namespace MondShield.Domain.Stages;

/// <summary>
/// Immutable configuration for a single stage. These are the rule parameters the broker
/// spec defines per level; all stage math reads them from here so there are no magic
/// numbers scattered through the codebase.
/// </summary>
/// <param name="Level">Which stage this config describes.</param>
/// <param name="CoverageRate">
/// Fraction (0–1) of a qualifying loss the broker covers at this stage. e.g. 0.50m = 50%.
/// </param>
/// <param name="BrokerShareRate">
/// Fraction (0–1) of withdrawn PROFIT the broker takes at this stage. e.g. 0.30m = 30%.
/// </param>
/// <param name="LevelUpProfitRate">
/// Fraction (0–1) of profit (relative to insured capital) required to advance one stage,
/// or <c>null</c> for the top stage (Vip) which has no level-up.
/// </param>
public sealed record StageConfig(
    StageLevel Level,
    decimal CoverageRate,
    decimal BrokerShareRate,
    decimal? LevelUpProfitRate)
{
    /// <summary>True when this stage can advance upward (every stage except Vip).</summary>
    public bool CanLevelUp => LevelUpProfitRate is not null;
}
