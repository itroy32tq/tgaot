namespace MtgaBot.Host.Shadow;

public sealed record ShadowRunResult(
    int EventCount,
    int DecisionCount,
    bool Followed);
