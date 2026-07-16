using MtgaBot.Decide;

namespace MtgaBot.Actuate;

public sealed class IntentExecutor(
    ICoordinateMap map,
    CalibrationProfile profile,
    IInputBackend input,
    IHoverObjectIdSource hoverSource,
    ActuatorBusy? busy = null,
    TimeSpan? hoverPointTimeout = null,
    TimeSpan? hoverMoveDelay = null)
    : IIntentExecutor
{
    private static readonly TimeSpan ClickGap = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan BlockDelay = TimeSpan.FromMilliseconds(800);

    private readonly TimeSpan _hoverPointTimeout = hoverPointTimeout ?? TimeSpan.FromMilliseconds(40);
    private readonly TimeSpan _hoverMoveDelay = hoverMoveDelay ?? TimeSpan.FromMilliseconds(10);

    public async Task<ActuateResult> ExecuteAsync(Intent intent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var name = intent.GetType().Name;
        var recorder = new RecordingProxyInputBackend(input);
        var hover = new HoverResolver(
            map,
            profile,
            recorder,
            hoverSource,
            _hoverPointTimeout,
            _hoverMoveDelay);

        using var _ = busy?.Enter();

        try
        {
            var ok = intent switch
            {
                PassPriorityIntent or ResolveIntent =>
                    await ClickButtonTwiceAsync(recorder, profile.Next, ct).ConfigureAwait(false),
                AttackAllIntent =>
                    await ClickButtonTwiceAsync(recorder, profile.AttackAll, ct).ConfigureAwait(false),
                KeepHandIntent keep =>
                    await ClickButtonAsync(
                        recorder,
                        keep.Keep ? profile.KeepHand : profile.Mulligan,
                        ct).ConfigureAwait(false),
                DeclareNoBlocksIntent =>
                    await NoBlocksAsync(recorder, ct).ConfigureAwait(false),
                AcknowledgeGroupIntent =>
                    await ClickButtonTwiceAsync(recorder, profile.Next, ct).ConfigureAwait(false),
                SelectTargetIntent target when target.InstanceId < 0 =>
                    await ClickButtonAsync(recorder, profile.OpponentAvatar, ct).ConfigureAwait(false),
                CastIntent cast =>
                    await hover.ClickHandCardAsync(cast.InstanceId, ct).ConfigureAwait(false),
                AttackWithIntent attack =>
                    await hover.ClickHandCardAsync(attack.InstanceId, ct).ConfigureAwait(false),
                SelectTargetIntent select =>
                    await hover.ClickHandCardAsync(select.InstanceId, ct).ConfigureAwait(false),
                NoOpIntent => true,
                _ => false,
            };

            var actions = (IReadOnlyList<UiAction>)recorder.Actions.ToList();
            return ok || intent is NoOpIntent
                ? new ActuateResult(true, name, actions)
                : new ActuateResult(false, name, actions, "Actuation failed or hover miss.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ActuateResult(false, name, recorder.Actions.ToList(), ex.Message);
        }
    }

    private async Task<bool> NoBlocksAsync(IInputBackend backend, CancellationToken ct)
    {
        await backend.ExecuteAsync(new DelayAction(BlockDelay), ct).ConfigureAwait(false);
        return await ClickButtonTwiceAsync(backend, profile.Next, ct).ConfigureAwait(false);
    }

    private async Task<bool> ClickButtonTwiceAsync(
        IInputBackend backend,
        DesignPoint design,
        CancellationToken ct)
    {
        await ClickButtonAsync(backend, design, ct).ConfigureAwait(false);
        await backend.ExecuteAsync(new DelayAction(ClickGap), ct).ConfigureAwait(false);
        await ClickButtonAsync(backend, design, ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ClickButtonAsync(
        IInputBackend backend,
        DesignPoint design,
        CancellationToken ct)
    {
        var (x, y) = map.ToScreen(design);
        await backend.ExecuteAsync(new MoveMouseAction(x, y), ct).ConfigureAwait(false);
        await backend.ExecuteAsync(new ClickAction(), ct).ConfigureAwait(false);
        return true;
    }
}
