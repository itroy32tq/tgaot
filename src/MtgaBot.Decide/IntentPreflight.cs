using MtgaBot.State;

namespace MtgaBot.Decide;

/// <summary>
/// Re-check that an Intent is still valid against the latest decision view before actuation.
/// </summary>
public static class IntentPreflight
{
    public static bool TryAccept(GameView decidedOn, Intent intent, GameView? current, out string? rejectReason)
    {
        ArgumentNullException.ThrowIfNull(decidedOn);
        ArgumentNullException.ThrowIfNull(intent);

        if (current is null)
        {
            rejectReason = "no current decision";
            return false;
        }

        if (current.Decision.DecisionId != decidedOn.Decision.DecisionId)
        {
            rejectReason =
                $"decision id changed ({decidedOn.Decision.DecisionId} → {current.Decision.DecisionId})";
            return false;
        }

        if (!IntentValidator.IsLegal(intent, current.Decision))
        {
            rejectReason = "intent no longer legal";
            return false;
        }

        if (GetHandTarget(intent) is { } targetId
            && !current.Board.HandInstanceIds.Contains(targetId))
        {
            rejectReason = $"instance {targetId} not in hand";
            return false;
        }

        rejectReason = null;
        return true;
    }

    public static int? GetHandTarget(Intent intent) => intent switch
    {
        PlayLandIntent play => play.InstanceId,
        CastIntent cast => cast.InstanceId,
        AttackWithIntent attack => attack.InstanceId,
        SelectTargetIntent select when select.InstanceId >= 0 => select.InstanceId,
        _ => null,
    };
}
