using MtgaBot.Ingest;
using MtgaBot.State;

namespace MtgaBot.State.Tests;

public class DecisionGateTests
{
    [Fact]
    public void CanDecide_WhenPendingMessages_BlocksMainPhase()
    {
        var gate = new DecisionGate();
        var snapshot = CreateSnapshot(pendingMessageCount: 1);
        var decision = CreateDecision(DecisionKind.MainPhase);

        Assert.False(gate.CanDecide(snapshot, decision, actuatorBusy: false));
    }

    [Fact]
    public void CanDecide_WhenPendingMessages_AllowsMulliganPrompt()
    {
        var gate = new DecisionGate();
        var snapshot = CreateSnapshot(pendingMessageCount: 2);
        var decision = CreateDecision(DecisionKind.Mulligan, [new LegalAction("Prompt", null, 1, null)]);

        Assert.True(gate.CanDecide(snapshot, decision, actuatorBusy: false));
    }

    [Fact]
    public void CanDecide_WhenMySeatAndActions_AllowsDecision()
    {
        var gate = new DecisionGate();
        var snapshot = CreateSnapshot(pendingMessageCount: 0);
        var decision = CreateDecision(DecisionKind.MainPhase);

        Assert.True(gate.CanDecide(snapshot, decision, actuatorBusy: false));
    }

    [Fact]
    public void CanDecide_WhenActuatorBusy_BlocksDecision()
    {
        var gate = new DecisionGate();
        var snapshot = CreateSnapshot(pendingMessageCount: 0);
        var decision = CreateDecision(DecisionKind.MainPhase);

        Assert.False(gate.CanDecide(snapshot, decision, actuatorBusy: true));
    }

    private static GameSnapshot CreateSnapshot(int pendingMessageCount) =>
        new(
            MySeatId: 1,
            Turn: new TurnInfo("Phase_Main1", "Step_Main", 1, 1, 1, 1),
            Objects: new Dictionary<int, CardView>(),
            HandInstanceIds: [101],
            BattlefieldInstanceIds: [],
            StackInstanceIds: [],
            MyLife: 20,
            OpponentLife: 20,
            Mana: ManaPool.Empty,
            PendingMessageCount: pendingMessageCount);

    private static DecisionPoint CreateDecision(
        DecisionKind kind,
        IReadOnlyList<LegalAction>? legalActions = null) =>
        new(
            DecisionId: 1,
            Kind: kind,
            SystemSeatId: 1,
            LegalActions: legalActions ?? [new LegalAction("ActionType_Cast", 101, 1, null)],
            Prompt: null);
}
