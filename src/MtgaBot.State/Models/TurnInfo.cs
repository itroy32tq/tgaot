namespace MtgaBot.State;

public sealed record TurnInfo(
    string Phase,
    string Step,
    int TurnNumber,
    int ActivePlayer,
    int PriorityPlayer,
    int DecisionPlayer);
