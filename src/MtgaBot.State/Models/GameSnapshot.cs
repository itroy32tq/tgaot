namespace MtgaBot.State;

public sealed record GameSnapshot(
    int MySeatId,
    TurnInfo Turn,
    IReadOnlyDictionary<int, CardView> Objects,
    IReadOnlyList<int> HandInstanceIds,
    IReadOnlyList<int> BattlefieldInstanceIds,
    IReadOnlyList<int> StackInstanceIds,
    int MyLife,
    int OpponentLife,
    ManaPool Mana,
    int PendingMessageCount);
