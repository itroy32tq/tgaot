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
    int hoverAttempts = 3,
    TimeSpan? landActuateBudget = null)
    : IIntentExecutor
{
    private static readonly TimeSpan ClickGap = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan BlockDelay = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan ButtonSettle = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan DragSettle = TimeSpan.FromMilliseconds(80);
    /// <summary>Hold after MouseDown so MTGA picks the card up before travel.</summary>
    private static readonly TimeSpan DragGrabHold = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan DragStepDelay = TimeSpan.FromMilliseconds(55);
    private static readonly TimeSpan DragDropHold = TimeSpan.FromMilliseconds(180);

    /// <summary>Coarser/faster inventory so land finishes inside our Main1.</summary>
    private const int LandInventoryMinStep = 32;

    private readonly TimeSpan _hoverPointTimeout = hoverPointTimeout ?? TimeSpan.FromMilliseconds(40);
    private readonly TimeSpan _hoverMoveDelay = hoverMoveDelay ?? TimeSpan.FromMilliseconds(10);
    private readonly TimeSpan _hoverResetDelay = hoverResetDelay ?? TimeSpan.FromMilliseconds(300);
    private readonly TimeSpan _hoverRetryPause = hoverRetryPause ?? TimeSpan.FromMilliseconds(800);
    private readonly int _hoverAttempts = Math.Max(1, hoverAttempts);
    private readonly TimeSpan? _landActuateBudgetOverride = landActuateBudget;

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
            bool ok;
            string? detailError = null;

            if (intent is PlayLandIntent play)
            {
                var land = await PlayLandViaInventoryAsync(recorder, play.InstanceId, ct)
                    .ConfigureAwait(false);
                ok = land.Ok;
                detailError = land.Error;
            }
            else
            {
                ok = intent switch
                {
                    PassPriorityIntent or ResolveIntent =>
                        await ClickButtonAsync(recorder, profile.Next, ct).ConfigureAwait(false),
                    AttackAllIntent =>
                        await ClickButtonTwiceAsync(recorder, profile.AttackAll, ct).ConfigureAwait(false),
                    KeepHandIntent keep =>
                        await ClickKeepOrMulliganAsync(
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
            }

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
                error: detailError
                       ?? (kind == ActuateOutcomeKind.HoverMiss
                           ? "Actuation failed or hover miss."
                           : "Actuation failed."),
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

    private readonly record struct LandActuateOutcome(bool Ok, string? Error);

    /// <summary>
    /// Full-arc inventory → PickFirst stub → drag up. Cancels cleanly if <paramref name="ct"/> fires
    /// (priority loss). Budget only covers the arc timing, not opponent's turn.
    /// </summary>
    private async Task<LandActuateOutcome> PlayLandViaInventoryAsync(
        IInputBackend backend,
        int playableLandId,
        CancellationToken ct)
    {
        var landStep = Math.Max(profile.HandScanStep, LandInventoryMinStep);
        // Faster than cast hover defaults — must finish while we still have Main1.
        var pointTimeout = TimeSpan.FromMilliseconds(Math.Min(25, _hoverPointTimeout.TotalMilliseconds));
        var moveDelay = TimeSpan.FromMilliseconds(Math.Min(5, _hoverMoveDelay.TotalMilliseconds));
        var resetDelay = TimeSpan.FromMilliseconds(Math.Min(120, _hoverResetDelay.TotalMilliseconds));

        var scanner = new HandInventoryScanner(
            map,
            profile,
            backend,
            hoverSource,
            pointTimeout,
            moveDelay,
            resetDelay,
            stepOverride: landStep);

        var budget = _landActuateBudgetOverride
                     ?? scanner.EstimateDuration + TimeSpan.FromSeconds(1);
        using var budgetCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budgetCts.CancelAfter(budget);
        var scanCt = budgetCts.Token;

        IReadOnlyList<HandCardHit> inventory;
        try
        {
            inventory = await scanner.ScanAsync(scanCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new LandActuateOutcome(false, "cancelled: lost Main1 priority during hand scan");
        }
        catch (OperationCanceledException)
        {
            return new LandActuateOutcome(
                false,
                $"land scan budget exceeded ({budget.TotalSeconds:0.0}s) before full arc; " +
                $"step={landStep} points≈{scanner.PointCount}.");
        }

        ct.ThrowIfCancellationRequested();

        var ids = string.Join(',', inventory.Select(h => h.InstanceId));
        var pick = LandPlayPicker.PickFirst(inventory, [playableLandId]);
        if (pick is null)
        {
            return new LandActuateOutcome(
                false,
                $"land {playableLandId} not in inventory [{ids}] (n={inventory.Count}). " +
                "Check hand_scan_points Y / hover parsing.");
        }

        // Confirm hover still matches at grab point before committing the drag.
        await backend.ExecuteAsync(new MoveMouseAction(pick.ScreenX, pick.ScreenY), ct).ConfigureAwait(false);
        await backend.ExecuteAsync(new DelayAction(TimeSpan.FromMilliseconds(80)), ct).ConfigureAwait(false);
        if (!await hoverSource.WaitForAsync(playableLandId, TimeSpan.FromMilliseconds(150), ct)
                .ConfigureAwait(false))
        {
            return new LandActuateOutcome(
                false,
                $"pre-drag hover miss for land {playableLandId} at x={pick.DesignX} (inventory had it).");
        }

        try
        {
            await DragLandUpAsync(backend, pick, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new LandActuateOutcome(false, "cancelled: lost Main1 priority during land drag");
        }
        catch (OperationCanceledException)
        {
            return new LandActuateOutcome(
                false,
                $"land drag cancelled after finding id={pick.InstanceId} at x={pick.DesignX}.");
        }

        return new LandActuateOutcome(true, null);
    }

    private async Task DragLandUpAsync(IInputBackend backend, HandCardHit hit, CancellationToken ct)
    {
        var dragUp = Math.Max(200, profile.LandDragUpDesign);
        // Grab at the inventory hover point we just confirmed — do not jump to a different Y
        // (that dropped the card and looked like a too-fast click/release).
        var grabX = hit.ScreenX;
        var grabY = hit.ScreenY;
        var dropDesignY = Math.Max(0, profile.HandScanP1.Y - dragUp);
        var (_, dropY) = map.ToScreen(new DesignPoint(hit.DesignX, dropDesignY));
        var dropX = grabX;

        var held = false;
        try
        {
            await backend.ExecuteAsync(new MoveMouseAction(grabX, grabY), ct).ConfigureAwait(false);
            await backend.ExecuteAsync(new DelayAction(DragSettle), ct).ConfigureAwait(false);
            await backend.ExecuteAsync(new MouseDownAction(), ct).ConfigureAwait(false);
            held = true;
            // MTGA needs a real press-hold before the card engages as a drag.
            await backend.ExecuteAsync(new DelayAction(DragGrabHold), ct).ConfigureAwait(false);

            // Small lift to engage drag, then stepped travel to the battlefield.
            var liftY = grabY - Math.Min(100, Math.Max(40, Math.Abs(grabY - dropY) / 4));
            await backend.ExecuteAsync(new MoveMouseAction(grabX, liftY), ct).ConfigureAwait(false);
            await backend.ExecuteAsync(new DelayAction(DragSettle), ct).ConfigureAwait(false);

            const int steps = 8;
            for (var i = 1; i <= steps; i++)
            {
                ct.ThrowIfCancellationRequested();
                var t = i / (double)steps;
                var x = grabX + (int)Math.Round((dropX - grabX) * t);
                var y = liftY + (int)Math.Round((dropY - liftY) * t);
                await backend.ExecuteAsync(new MoveMouseAction(x, y), ct).ConfigureAwait(false);
                await backend.ExecuteAsync(new DelayAction(DragStepDelay), ct).ConfigureAwait(false);
            }

            await backend.ExecuteAsync(new DelayAction(DragDropHold), ct).ConfigureAwait(false);
            await backend.ExecuteAsync(new MouseUpAction(), ct).ConfigureAwait(false);
            held = false;
        }
        finally
        {
            if (held)
            {
                try
                {
                    await backend.ExecuteAsync(new MouseUpAction(), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // best-effort release
                }
            }
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

    private async Task<bool> ClickKeepOrMulliganAsync(
        IInputBackend backend,
        DesignPoint design,
        CancellationToken ct)
    {
        // One deliberate click (settle + down/up). A second click often lands after GRE already kept.
        await ClickButtonAsync(backend, design, ct).ConfigureAwait(false);
        return true;
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
        await backend.ExecuteAsync(new DelayAction(ButtonSettle), ct).ConfigureAwait(false);
        await backend.ExecuteAsync(new MouseDownAction(), ct).ConfigureAwait(false);
        await backend.ExecuteAsync(new DelayAction(TimeSpan.FromMilliseconds(40)), ct).ConfigureAwait(false);
        await backend.ExecuteAsync(new MouseUpAction(), ct).ConfigureAwait(false);
        return true;
    }
}
