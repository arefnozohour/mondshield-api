namespace MondShield.Domain.Stages;

/// <summary>
/// The six MondShield levels, ordered from lowest to highest. Numeric values encode the
/// ladder order so "up" / "down" comparisons are simple integer steps — do NOT reorder or
/// renumber, persistence and transition logic rely on these values.
/// </summary>
/// <remarks>
/// The broker diagram reads right-to-left (Farsi); here we list them low → high.
/// Farsi names: Revival = احیا, Rebuild = بازسازی, Stage1 = مرحله اول, Stage2 = مرحله دوم,
/// Stage3 = مرحله سوم, Vip = قله طلایی (Golden Peak).
/// </remarks>
public enum StageLevel
{
    /// <summary>احیا — last chance to stay in the program. A compensation here exits the trader.</summary>
    Revival = 0,

    /// <summary>بازسازی — the floor that down-transitions from Stage 1+ land on.</summary>
    Rebuild = 1,

    /// <summary>مرحله اول — the standard activation entry point ($2,000 deposit).</summary>
    Stage1 = 2,

    /// <summary>مرحله دوم.</summary>
    Stage2 = 3,

    /// <summary>مرحله سوم.</summary>
    Stage3 = 4,

    /// <summary>قله طلایی (Golden Peak) — top level. No level-up; 0% broker share.</summary>
    Vip = 5,
}
