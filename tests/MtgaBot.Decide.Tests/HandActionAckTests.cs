using MtgaBot.State;

namespace MtgaBot.Decide.Tests;

public class HandActionAckTests
{
    [Fact]
    public void Confirmed_WhenIdLeftHand()
    {
        var board = Board(hand: [11, 12]);
        Assert.True(HandActionAck.IsConfirmed(board, Decision([new LegalAction("ActionType_Play", 10, 1, null)]), 10));
    }

    [Fact]
    public void NotConfirmed_WhenStillInHand_EvenIfPlayGoneFromLegal()
    {
        var board = Board(hand: [10]);
        Assert.False(HandActionAck.IsConfirmed(board, Decision([new LegalAction("ActionType_Pass", null, 1, null)]), 10));
    }

    [Fact]
    public void NotConfirmed_WhenStillInHandAndPlayLegal()
    {
        var board = Board(hand: [10]);
        Assert.False(HandActionAck.IsConfirmed(board, Decision([new LegalAction("ActionType_Play", 10, 1, null)]), 10));
    }

    [Fact]
    public void IsLeftHand_RequiresBeforeAndAfter()
    {
        var before = Board(hand: [10, 11]);
        var after = Board(hand: [11]);
        Assert.True(HandActionAck.IsLeftHand(before, after, 10));
        Assert.False(HandActionAck.IsLeftHand(before, after, 11));
        Assert.False(HandActionAck.IsLeftHand(before, before, 10));
    }

    private static GameSnapshot Board(IReadOnlyList<int> hand) =>
        new(
            1,
            new TurnInfo("Phase_Main1", "Step_Begin", 1, 1, 1, 1),
            hand.ToDictionary(id => id, id => new CardView(id, 1, 1, "ZoneType_Hand", 1, false)),
            hand,
            [],
            [],
            20,
            20,
            ManaPool.Empty,
            0);

    private static DecisionPoint Decision(IReadOnlyList<LegalAction> actions) =>
        new(1, DecisionKind.MainPhase, 1, actions, null);
}
