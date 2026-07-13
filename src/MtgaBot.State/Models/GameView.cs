namespace MtgaBot.State;

public sealed record GameView(
    GameSnapshot Board,
    DecisionPoint Decision,
    MatchPhase Lifecycle);
