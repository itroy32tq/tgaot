using MtgaBot.State;

namespace MtgaBot.Decide;

public static class IntentValidator
{
    /// <summary>
    /// Ensures Cast/AttackWith Intents reference an instanceId present in LegalActions.
    /// Pass/AttackAll/etc. are always considered legal.
    /// </summary>
    public static bool IsLegal(Intent intent, DecisionPoint decision)
    {
        return intent switch
        {
            CastIntent cast => HasInstance(decision, cast.InstanceId, "ActionType_Play", "ActionType_Cast", "Play", "Cast"),
            AttackWithIntent attack => HasInstance(decision, attack.InstanceId),
            SelectTargetIntent target when target.InstanceId < 0 => true,
            SelectTargetIntent target =>
                decision.Prompt?.ValidTargets is not { } valid || valid.Contains(target.InstanceId),
            _ => true,
        };
    }

    private static bool HasInstance(DecisionPoint decision, int instanceId, params string[] actionTypes)
    {
        foreach (var action in decision.LegalActions)
        {
            if (action.InstanceId != instanceId)
            {
                continue;
            }

            if (actionTypes.Length == 0)
            {
                return true;
            }

            foreach (var expected in actionTypes)
            {
                if (string.Equals(action.ActionType, expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
