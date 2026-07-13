namespace MtgaBot.State;

public sealed record DecisionPoint(
    ulong DecisionId,
    DecisionKind Kind,
    int SystemSeatId,
    IReadOnlyList<LegalAction> LegalActions,
    PromptContext? Prompt);
