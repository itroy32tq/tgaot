using MtgaBot.State;

namespace MtgaBot.Decide.Tests;

public class IntentPreflightTests
{
    [Fact]
    public void Accepts_SameDecision_LegalPlay_InHand()
    {
        var view = MainView(
            decisionId: 7,
            actions: [new LegalAction("ActionType_Play", 10, 1, null)],
            hand: [10]);

        Assert.True(IntentPreflight.TryAccept(view, new PlayLandIntent(10), view, out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void Rejects_DecisionIdChanged()
    {
        var decided = MainView(7, [new LegalAction("ActionType_Play", 10, 1, null)], [10]);
        var current = MainView(8, [new LegalAction("ActionType_Play", 10, 1, null)], [10]);

        Assert.False(IntentPreflight.TryAccept(decided, new PlayLandIntent(10), current, out var reason));
        Assert.Contains("decision id changed", reason);
    }

    [Fact]
    public void Rejects_NotInHand()
    {
        var view = MainView(7, [new LegalAction("ActionType_Play", 10, 1, null)], hand: []);

        Assert.False(IntentPreflight.TryAccept(view, new PlayLandIntent(10), view, out var reason));
        Assert.Contains("not in hand", reason);
    }

    [Fact]
    public void Rejects_NoLongerLegal()
    {
        var decided = MainView(7, [new LegalAction("ActionType_Play", 10, 1, null)], [10]);
        var current = MainView(7, [new LegalAction("ActionType_Pass", null, 1, null)], [10]);

        Assert.False(IntentPreflight.TryAccept(decided, new PlayLandIntent(10), current, out var reason));
        Assert.Equal("intent no longer legal", reason);
    }

    [Fact]
    public void Validator_PlayLand_RequiresPlayAction()
    {
        var decision = new DecisionPoint(
            1,
            DecisionKind.MainPhase,
            1,
            [new LegalAction("ActionType_Cast", 10, 1, null)],
            null);

        Assert.False(IntentValidator.IsLegal(new PlayLandIntent(10), decision));
        Assert.True(IntentValidator.IsLegal(new CastIntent(10), decision));
    }

    private static GameView MainView(ulong decisionId, IReadOnlyList<LegalAction> actions, IReadOnlyList<int> hand) =>
        new(
            new GameSnapshot(
                1,
                new TurnInfo("Phase_Main1", "Step_Begin", 1, 1, 1, 1),
                hand.ToDictionary(id => id, id => new CardView(id, 1, 1, "ZoneType_Hand", 1, false)),
                hand,
                [],
                [],
                20,
                20,
                ManaPool.Empty,
                0),
            new DecisionPoint(decisionId, DecisionKind.MainPhase, 1, actions, null),
            MatchPhase.InMatch);
}
