namespace MtgaBot.Decide;

public static class IntentFormatter
{
    public static string Format(Intent intent) => intent switch
    {
        PlayLandIntent play => $"PlayLandIntent({play.InstanceId})",
        CastIntent cast => $"CastIntent({cast.InstanceId})",
        AttackAllIntent => "AttackAllIntent",
        AttackWithIntent attack => $"AttackWithIntent({attack.InstanceId})",
        PassPriorityIntent => "PassPriorityIntent",
        ResolveIntent => "ResolveIntent",
        SelectTargetIntent target => $"SelectTargetIntent({target.InstanceId})",
        KeepHandIntent keep => $"KeepHandIntent({keep.Keep})",
        DeclareNoBlocksIntent => "DeclareNoBlocksIntent",
        AcknowledgeGroupIntent => "AcknowledgeGroupIntent",
        NoOpIntent noOp => $"NoOpIntent({noOp.Reason})",
        _ => intent.GetType().Name,
    };
}
