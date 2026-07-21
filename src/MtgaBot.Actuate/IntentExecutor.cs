using System.Diagnostics;
using MtgaBot.Decide;

namespace MtgaBot.Actuate;

public sealed class IntentExecutor(
    ICoordinateMap map,
    CalibrationProfile profile,
    IInputBackend input,
    IHoverObjectIdSource hoverSource,
    ActuatorBusy? busy = null,
    TimeSpan? hoverPointTimeout = null,
    TimeSpan? hoverMoveDelay = null,
    TimeSpan? hoverResetDelay = null,
    TimeSpan? hoverRetryPause = null,
    int hoverAttempts = 3)
    : IIntentExecutor
{
    private static readonly TimeSpan ClickGap = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan BlockDelay = TimeSpan.FromMilliseconds(800);

    private readonly TimeSpan _hoverPointTimeout = hoverPointTimeout ?? TimeSpan.FromMilliseconds(40);
    private readonly TimeSpan _hoverMoveDelay = hoverMoveDelay ?? TimeSpan.FromMilliseconds(10);
    private readonly TimeSpan _hoverResetDelay = hoverResetDelay ?? TimeSpan.FromMilliseconds(300);
    private readonly TimeSpan _hoverRetryPause = hoverRetryPause ?? TimeSpan.FromMilliseconds(800);
    private readonly int _hoverAttempts = Math.Max(1, hoverAttempts);

    public async Task<ActuateResult> ExecuteAsync(Intent intent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var name = intent.GetType().Name;
        var targetId = IntentPreflight.GetHandTarget(intent);
        var sw = Stopwatch.StartNew();
        var recorder = new RecordingProxyInputBackend(input);
        var hover = new HoverResolver(
            map,
            profile,
            recorder,
            hoverSource,
            _hoverPointTimeout,
            _hoverMoveDelay,
            _hoverResetDelay);

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
                PlayLandIntent play =>
                    await ClickHandWithRetryAsync(
                        hover,
                        recorder,
                        play.InstanceId,
                        HandClickProfile.SingleClick,
                        ct).ConfigureAwait(false),
                CastIntent cast =>
                    await ClickHandWithRetryAsync(
                        hover,
                        recorder,
                        cast.InstanceId,
                        HandClickProfile.DoubleClick,
                        ct).ConfigureAwait(false),
                AttackWithIntent attack =>
                    await ClickHandWithRetryAsync(
                        hover,
                        recorder,
                        attack.InstanceId,
                        HandClickProfile.DoubleClick,
                        ct).ConfigureAwait(false),
                SelectTargetIntent select =>
                    await ClickHandWithRetryAsync(
                        hover,
                        recorder,
                        select.InstanceId,
                        HandClickProfile.DoubleClick,
                        ct).ConfigureAwait(false),
                NoOpIntent => true,
                _ => false,
            };

            var actions = (IReadOnlyList<UiAction>)recorder.Actions.ToList();
            sw.Stop();

            if (intent is NoOpIntent)
            {
                return ActuateResult.FromKind(
                    ActuateOutcomeKind.Skipped,
                    name,
                    actions,
                    elapsedMs: sw.ElapsedMilliseconds);
            }

            if (ok)
            {
                return ActuateResult.FromKind(
                    ActuateOutcomeKind.UiSucceeded,
                    name,
                    actions,
                    targetInstanceId: targetId,
                    elapsedMs: sw.ElapsedMilliseconds);
            }

            var kind = targetId is not null ? ActuateOutcomeKind.HoverMiss : ActuateOutcomeKind.Failed;
            return ActuateResult.FromKind(
                kind,
                name,
                actions,
                error: kind == ActuateOutcomeKind.HoverMiss
                    ? "Actuation failed or hover miss."
                    : "Actuation failed.",
                targetInstanceId: targetId,
                elapsedMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            return ActuateResult.FromKind(
                ActuateOutcomeKind.Failed,
                name,
                recorder.Actions.ToList(),
                error: ex.Message,
                targetInstanceId: targetId,
                elapsedMs: sw.ElapsedMilliseconds);
        }
    }

    private async Task<bool> ClickHandWithRetryAsync(
        IHoverResolver hover,
        IInputBackend backend,
        int instanceId,
        HandClickProfile clickProfile,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < _hoverAttempts; attempt++)
        {
            if (await hover.ClickHandCardAsync(instanceId, ct, clickProfile).ConfigureAwait(false))
            {
                return true;
            }

            if (attempt + 1 < _hoverAttempts)
            {
                await backend.ExecuteAsync(new DelayAction(_hoverRetryPause), ct).ConfigureAwait(false);
            }
        }

        return false;
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
