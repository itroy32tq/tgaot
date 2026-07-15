namespace MtgaBot.Decide;

public abstract record Intent;

public sealed record CastIntent(int InstanceId) : Intent;

public sealed record AttackAllIntent : Intent;

public sealed record AttackWithIntent(int InstanceId) : Intent;

public sealed record PassPriorityIntent : Intent;

public sealed record ResolveIntent : Intent;

public sealed record SelectTargetIntent(int InstanceId) : Intent;

public sealed record KeepHandIntent(bool Keep) : Intent;

public sealed record DeclareNoBlocksIntent : Intent;

public sealed record AcknowledgeGroupIntent : Intent;

public sealed record NoOpIntent(string Reason) : Intent;
