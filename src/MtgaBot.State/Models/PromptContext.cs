namespace MtgaBot.State;

public sealed record PromptContext(
    int? MinSelections,
    int? MaxSelections,
    IReadOnlyList<int>? ValidTargets);
