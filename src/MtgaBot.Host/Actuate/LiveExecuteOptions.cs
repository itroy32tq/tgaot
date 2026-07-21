using MtgaBot.Decide;

namespace MtgaBot.Host.Actuate;

public sealed record LiveExecuteOptions(
    string LogPath,
    string PolicyName = "FarmMvp",
    string? CardsPath = null,
    string? CardsOverlayPath = null,
    string? CalibrationPath = null,
    bool DryRun = false,
    FarmMvpMode Mode = FarmMvpMode.FullMvp,
    string? AttemptLogPath = null);
