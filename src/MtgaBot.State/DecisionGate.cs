namespace MtgaBot.State;

public sealed class DecisionGate
{
    public bool CanDecide(GameSnapshot snapshot, DecisionPoint decision, bool actuatorBusy)
    {
        return snapshot.PendingMessageCount == 0
            && decision.Kind != DecisionKind.None
            && decision.LegalActions.Count > 0
            && decision.SystemSeatId == snapshot.MySeatId
            && !actuatorBusy;
    }
}
