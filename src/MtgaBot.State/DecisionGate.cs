namespace MtgaBot.State;

public sealed class DecisionGate
{
    public bool CanDecide(GameSnapshot snapshot, DecisionPoint decision, bool actuatorBusy)
    {
        if (decision.Kind == DecisionKind.None
            || decision.LegalActions.Count == 0
            || decision.SystemSeatId != snapshot.MySeatId
            || actuatorBusy)
        {
            return false;
        }

        // Explicit GRE prompts (mulligan/attackers/…) arrive in the same log line as a
        // GameState Diff that still has pendingMessageCount>0. Blocking on pending here
        // suppresses KeepHandIntent entirely.
        if (IsExplicitPrompt(decision.Kind))
        {
            return true;
        }

        // Main1/Main2 often stay at pendingMessageCount>0 while legal Play/Cast are already
        // valid (live: pending=2, legal=7 — land scan never started). Preflight still
        // re-checks before actuate.
        if (decision.Kind == DecisionKind.MainPhase)
        {
            return true;
        }

        return snapshot.PendingMessageCount == 0;
    }

    private static bool IsExplicitPrompt(DecisionKind kind) =>
        kind is DecisionKind.Mulligan
            or DecisionKind.SelectTargets
            or DecisionKind.Attackers
            or DecisionKind.Blockers
            or DecisionKind.SelectN
            or DecisionKind.GroupReq
            or DecisionKind.PayCosts
            or DecisionKind.CastingTimeOptions
            or DecisionKind.AssignDamage;
}
