using MtgaBot.State;

namespace MtgaBot.Decide;

/// <summary>Always pass / resolve — useful as a safe shadow baseline.</summary>
public sealed class PassPolicy : IPolicy
{
    public const string Name = "Pass";

    public Intent Decide(GameView view, ICardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(view);
        return view.Decision.Kind switch
        {
            DecisionKind.Mulligan => new KeepHandIntent(true),
            DecisionKind.Attackers => new AttackAllIntent(),
            DecisionKind.Blockers => new DeclareNoBlocksIntent(),
            DecisionKind.GroupReq => new AcknowledgeGroupIntent(),
            _ => new PassPriorityIntent(),
        };
    }
}
