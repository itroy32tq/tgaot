namespace MtgaBot.Actuate;

/// <summary>
/// UI points in design space (default 1920×1080 client-relative).
/// </summary>
public sealed record CalibrationProfile(
    int DesignWidth,
    int DesignHeight,
    DesignPoint KeepHand,
    DesignPoint Mulligan,
    DesignPoint Next,
    DesignPoint AttackAll,
    DesignPoint OpponentAvatar,
    DesignPoint HandScanP1,
    DesignPoint HandScanP2,
    int HandScanStep = 10,
    int LandDragUpDesign = 750)
{
    public static CalibrationProfile CreateDefault() => new(
        DesignWidth: 1920,
        DesignHeight: 1080,
        KeepHand: new DesignPoint(1101, 870),
        Mulligan: new DesignPoint(801, 870),
        Next: new DesignPoint(1755, 944),
        AttackAll: new DesignPoint(1755, 944),
        OpponentAvatar: new DesignPoint(1286, 216),
        HandScanP1: new DesignPoint(0, 1050),
        HandScanP2: new DesignPoint(1920, 1050),
        HandScanStep: 10,
        LandDragUpDesign: 750);
}

public readonly record struct DesignPoint(int X, int Y);
