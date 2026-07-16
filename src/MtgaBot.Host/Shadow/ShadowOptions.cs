namespace MtgaBot.Host.Shadow;

public sealed record ShadowOptions(
    string LogPath,
    bool Follow,
    string PolicyName = "FarmMvp",
    string? CardsPath = null,
    string? CardsOverlayPath = null);
