namespace MtgaBot.Decide;

/// <summary>
/// Incremental farm capability. Each mode unlocks the next without changing earlier paths.
/// </summary>
public enum FarmMvpMode
{
    /// <summary>Keep, PlayLand, Pass (and non-hand prompts that avoid hanging).</summary>
    LandOnly = 0,

    /// <summary><see cref="LandOnly"/> + safe Cast.</summary>
    LandAndCast = 1,

    /// <summary><see cref="LandAndCast"/> + AttackAll (default MVP).</summary>
    FullMvp = 2,
}
