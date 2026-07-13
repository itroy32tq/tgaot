namespace MtgaBot.State;

public enum DecisionKind
{
    None,
    MainPhase,
    Mulligan,
    SelectTargets,
    Attackers,
    Blockers,
    SelectN,
    GroupReq,
    PayCosts,
    CastingTimeOptions,
    AssignDamage,
}
