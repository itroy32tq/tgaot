using MtgaBot.Actuate;
using MtgaBot.Decide;

namespace MtgaBot.Host.Actuate;

public static class ActuateDryRun
{
    public static async Task<ActuateResult> PlanAsync(Intent intent, string? calibrationPath = null)
    {
        var profile = CalibrationLoader.Load(calibrationPath);
        var rect = FixedWindowLocator.DesignDefault.FindMtgaClientRect()
                   ?? new WindowRect(0, 0, 1920, 1080);
        var map = new CoordinateMap(rect, profile);
        var executor = new IntentExecutor(
            map,
            profile,
            new RecordingInputBackend(),
            new ImmediateHoverObjectIdSource(),
            hoverPointTimeout: TimeSpan.FromMilliseconds(1),
            hoverMoveDelay: TimeSpan.Zero);

        return await executor.ExecuteAsync(intent, CancellationToken.None).ConfigureAwait(false);
    }

    public static Intent ParseIntent(string name, int? instanceId)
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "pass" or "passpriority" or "passpriorityintent" => new PassPriorityIntent(),
            "resolve" or "resolveintent" => new ResolveIntent(),
            "attack" or "attackall" or "attackallintent" => new AttackAllIntent(),
            "keep" or "keephand" or "keephandintent" => new KeepHandIntent(true),
            "mulligan" => new KeepHandIntent(false),
            "noblocks" or "declarenoblocks" or "declarenoblocksintent" => new DeclareNoBlocksIntent(),
            "group" or "acknowledge" or "acknowledgegroupintent" => new AcknowledgeGroupIntent(),
            "cast" or "castintent" => new CastIntent(
                instanceId ?? throw new ArgumentException("Cast requires --instance <id>.")),
            "play" or "playland" or "playlandintent" => new PlayLandIntent(
                instanceId ?? throw new ArgumentException("PlayLand requires --instance <id>.")),
            "target" or "selecttarget" or "selecttargetintent" => new SelectTargetIntent(
                instanceId ?? throw new ArgumentException("SelectTarget requires --instance <id>.")),
            _ => throw new ArgumentException(
                $"Unknown intent '{name}'. Try: pass, attack, keep, mulligan, noblocks, play, cast, target."),
        };
    }
}
