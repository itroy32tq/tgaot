using MtgaBot.State;

namespace MtgaBot.State.Tests;

public class LegalActionFilterTests
{
    [Fact]
    public void PruneAgainstBoard_DropsPlayCastNotInHand()
    {
        var board = new GameSnapshot(
            MySeatId: 1,
            Turn: new TurnInfo("Phase_Main1", "Step_Begin", 1, 1, 1, 1),
            Objects: new Dictionary<int, CardView>
            {
                [10] = new(10, 1, 1, "ZoneType_Hand", 1, false),
                [20] = new(20, 2, 2, "ZoneType_Battlefield", 1, false),
            },
            HandInstanceIds: [10],
            BattlefieldInstanceIds: [20],
            StackInstanceIds: [],
            MyLife: 20,
            OpponentLife: 20,
            Mana: ManaPool.Empty,
            PendingMessageCount: 0);

        var actions = new List<LegalAction>
        {
            new("ActionType_Play", 20, 1, null), // already on BF
            new("ActionType_Cast", 10, 1, null),
            new("ActionType_Cast", 99, 1, null), // gone
            new("ActionType_Pass", null, 1, null),
            new("ActionType_Activate_Mana", 20, 1, null),
        };

        var pruned = LegalActionFilter.PruneAgainstBoard(actions, board);

        Assert.Equal(3, pruned.Count);
        Assert.Contains(pruned, a => a is { ActionType: "ActionType_Cast", InstanceId: 10 });
        Assert.Contains(pruned, a => a.ActionType == "ActionType_Pass");
        Assert.Contains(pruned, a => a is { ActionType: "ActionType_Activate_Mana", InstanceId: 20 });
        Assert.DoesNotContain(pruned, a => a.InstanceId == 20 && a.ActionType.Contains("Play"));
        Assert.DoesNotContain(pruned, a => a.InstanceId == 99);
    }

    [Fact]
    public void BuildSignature_ChangesWhenLegalActionsChange()
    {
        var board = new GameSnapshot(
            1,
            new TurnInfo("Phase_Main1", "Step_Begin", 1, 1, 1, 1),
            new Dictionary<int, CardView>(),
            [10],
            [],
            [],
            20,
            20,
            ManaPool.Empty,
            0);

        var left = new GameView(
            board,
            new DecisionPoint(1, DecisionKind.MainPhase, 1, [new LegalAction("ActionType_Play", 10, 1, null)], null),
            MatchPhase.InMatch);
        var right = new GameView(
            board,
            new DecisionPoint(2, DecisionKind.MainPhase, 1, [new LegalAction("ActionType_Pass", null, 1, null)], null),
            MatchPhase.InMatch);

        Assert.NotEqual(
            LegalActionFilter.BuildSignature(left),
            LegalActionFilter.BuildSignature(right));
        Assert.Equal(
            LegalActionFilter.BuildSignature(left),
            LegalActionFilter.BuildSignature(left with
            {
                Decision = left.Decision with { DecisionId = 99 },
            }));
    }
}

public class PromptTrackerTests
{
    [Fact]
    public void ApplyGameStateActions_Empty_ClearsStickyPrompt()
    {
        var tracker = new PromptTracker();
        tracker.ApplyGameStateActions(
            [new LegalAction("ActionType_Play", 1, 1, null)],
            seatId: 1,
            new TurnInfo("Phase_Main1", "Step_Begin", 1, 1, 1, 1));

        Assert.NotNull(tracker.BuildDecisionPoint(1));

        tracker.ApplyGameStateActions([], seatId: 1, new TurnInfo("Phase_Main1", "Step_Begin", 1, 1, 1, 1));

        Assert.Null(tracker.BuildDecisionPoint(2));
    }

    [Fact]
    public void ApplyGreRequest_Attackers_NotOverwrittenByCastList()
    {
        var tracker = new PromptTracker();
        using var req = System.Text.Json.JsonDocument.Parse(
            """{ "type": "GREMessageType_DeclareAttackersReq", "systemSeatIds": [1] }""");
        tracker.ApplyGreRequest("GREMessageType_DeclareAttackersReq", req.RootElement);

        tracker.ApplyGameStateActions(
            [new LegalAction("ActionType_Cast", 10, 1, null)],
            seatId: 1,
            new TurnInfo("Phase_Combat", "Step_DeclareAttack", 1, 1, 1, 1));

        var decision = tracker.BuildDecisionPoint(5);
        Assert.NotNull(decision);
        Assert.Equal(DecisionKind.Attackers, decision.Kind);
        Assert.Equal("Prompt", decision.LegalActions[0].ActionType);
    }

    [Fact]
    public void ApplyGameStateActions_TurnZeroUnknown_DoesNotOpenMainPhase()
    {
        var tracker = new PromptTracker();
        tracker.ApplyGameStateActions(
            [new LegalAction("ActionType_Play", 163, 1, null)],
            seatId: 1,
            new TurnInfo("Phase_Unknown", "Step_Unknown", 0, 0, 0, 1));

        Assert.Null(tracker.BuildDecisionPoint(1));
    }

    [Fact]
    public void MulliganReq_ThenOpeningHandActions_KeepsMulliganDecision()
    {
        var tracker = new PromptTracker();
        using var req = System.Text.Json.JsonDocument.Parse(
            """{ "type": "GREMessageType_MulliganReq", "systemSeatIds": [1], "mulliganReq": { "mulliganType": "MulliganType_London" } }""");
        tracker.ApplyGreRequest("GREMessageType_MulliganReq", req.RootElement);

        tracker.ApplyGameStateActions(
            [new LegalAction("ActionType_Play", 163, 1, null)],
            seatId: 1,
            new TurnInfo("Phase_Unknown", "Step_Unknown", 0, 2, 0, 1));

        var decision = tracker.BuildDecisionPoint(6);
        Assert.NotNull(decision);
        Assert.Equal(DecisionKind.Mulligan, decision.Kind);
    }

    [Fact]
    public void ApplyGameStateActions_DeclareAttackStep_UsesPromptNotCastList()
    {
        var tracker = new PromptTracker();
        tracker.ApplyGameStateActions(
            [new LegalAction("ActionType_Cast", 10, 1, null)],
            seatId: 1,
            new TurnInfo("Phase_Combat", "Step_DeclareAttack", 1, 1, 1, 1));

        var decision = tracker.BuildDecisionPoint(7);
        Assert.NotNull(decision);
        Assert.Equal(DecisionKind.Attackers, decision.Kind);
        Assert.Equal("Prompt", decision.LegalActions[0].ActionType);
    }
}
