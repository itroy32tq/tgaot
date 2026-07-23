namespace MtgaBot.State.Tests;

public class PriorityWindowTests
{
    [Fact]
    public void IsOurMain1_RequiresOurActiveMain1AndPriority()
    {
        var board = new GameSnapshot(
            MySeatId: 1,
            Turn: new TurnInfo("Phase_Main1", "Step_Main", 1, ActivePlayer: 1, PriorityPlayer: 1, DecisionPlayer: 1),
            Objects: new Dictionary<int, CardView>(),
            HandInstanceIds: [1],
            BattlefieldInstanceIds: [],
            StackInstanceIds: [],
            MyLife: 20,
            OpponentLife: 20,
            Mana: ManaPool.Empty,
            PendingMessageCount: 0);

        Assert.True(PriorityWindow.IsOurMain1(board));

        var opponentTurn = board with
        {
            Turn = board.Turn with { PriorityPlayer = 2, ActivePlayer = 2, DecisionPlayer = 2 },
        };
        Assert.False(PriorityWindow.IsOurMain1(opponentTurn));

        // Opponent is active (their Main1) but we briefly hold priority — still not our land window.
        var ourPrioOnOppMain1 = board with
        {
            Turn = board.Turn with
            {
                TurnNumber = 2,
                ActivePlayer = 2,
                PriorityPlayer = 1,
                DecisionPlayer = 1,
            },
        };
        Assert.False(PriorityWindow.IsOurMain1(ourPrioOnOppMain1));

        var beginning = board with
        {
            Turn = board.Turn with { Phase = "Phase_Beginning", Step = "Step_Upkeep" },
        };
        Assert.False(PriorityWindow.IsOurMain1(beginning));
        Assert.False(PriorityWindow.IsOurTurnMain1(beginning));

        Assert.True(PriorityWindow.IsOurTurnMain1(board));
        Assert.True(PriorityWindow.IsOurTurnMain1(board with
        {
            Turn = board.Turn with { PriorityPlayer = 2 },
        }));
        Assert.False(PriorityWindow.IsOurTurnMain1(opponentTurn));
    }
}
