namespace MtgaBot.Host.Shadow;

public sealed record ShadowOptions(
    string LogPath,
    bool Follow,
    string PolicyName = "FarmMvp");
