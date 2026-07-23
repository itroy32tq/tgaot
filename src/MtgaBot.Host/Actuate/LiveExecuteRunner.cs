using System.Threading.Channels;
using MtgaBot.Actuate;
using MtgaBot.Actuate.Windows;
using MtgaBot.Decide;
using MtgaBot.Ingest;
using MtgaBot.State;

namespace MtgaBot.Host.Actuate;

/// <summary>
/// Phase 3 live loop: concurrent log tail (GRE + hover) → State → Decide → Actuate.
/// </summary>
public sealed class LiveExecuteRunner
{
    private const long CatchUpMaxBytes = 2_000_000;
    private static readonly TimeSpan LandAckBudget = TimeSpan.FromSeconds(2.5);

    private readonly GreLogTailer _tailer;
    private readonly GreLogParser _parser;
    private readonly ILiveExecuteReporter _reporter;
    private readonly IPolicy _policy;
    private readonly ICardDatabase _cards;
    private readonly CardDatabaseResolver.ResolveResult _cardsMeta;
    private readonly HashSet<(ulong DecisionId, string Intent)> _actuatedKeys = [];
    private DateTimeOffset? _keepClickSentAt;
    private int _keepClickAttempts;
    private int? _pendingLandAckId;
    private DateTimeOffset? _pendingLandAckSince;
    private int _landDragRetries;
    private ulong? _pendingLandDecisionId;
    private bool _requeueLandRetry;
    private int _lastObservedTurnNumber = -1;

    private sealed class SequenceHolder(ulong value)
    {
        public ulong Value { get; set; } = value;
    }

    public LiveExecuteRunner(ILiveExecuteReporter reporter, LiveExecuteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var resolved = CardDatabaseResolver.Resolve(options.CardsPath, options.CardsOverlayPath);
        _tailer = new GreLogTailer();
        _parser = new GreLogParser();
        _reporter = reporter;
        _policy = PolicyFactory.Create(options.PolicyName, options.Mode);
        _cards = resolved.Database;
        _cardsMeta = resolved;
    }

    public async Task RunAsync(LiveExecuteOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LogPath);

        if (!File.Exists(options.LogPath))
        {
            _reporter.OnError($"Player.log not found: {options.LogPath}");
            throw new FileNotFoundException("Player.log not found.", options.LogPath);
        }

        var locator = new Win32WindowLocator();
        var window = locator.FindMtgaClientRect();
        var calibrationPath = CalibrationLoader.ResolvePath(options.CalibrationPath);
        _reporter.OnStarted(options, _cardsMeta, window, calibrationPath);

        var profile = CalibrationLoader.Load(options.CalibrationPath);
        var hover = new LogHoverObjectIdSource();
        var busy = new ActuatorBusy();
        var engine = new StateEngine();

        GameView? pending = null;
        var catchUp = true;
        engine.DecisionReady += view =>
        {
            if (!catchUp)
            {
                pending = view;
            }
        };

        var recent = _tailer.ParseRecent(options.LogPath, CatchUpMaxBytes);
        var sequence = new SequenceHolder(0);
        foreach (var evt in recent.Events)
        {
            ct.ThrowIfCancellationRequested();
            sequence.Value++;
            engine.Apply(evt with { Sequence = sequence.Value });
        }

        if (engine.TryGetSnapshot() is { } catchUpSnap)
        {
            hover.SetMySeatId(catchUpSnap.MySeatId);
            NoteTurnBoundary(catchUpSnap.Turn.TurnNumber);
        }

        catchUp = false;
        _reporter.OnInfo(
            $"catch-up: {recent.Events.Count} GRE events from last {recent.BytesRead / 1024} KB");

        var greChannel = Channel.CreateUnbounded<GreEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var resumeOffset = recent.ResumeOffset;
        var tailTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var line in _tailer
                                   .TailRawLines(options.LogPath, ct, resumeOffset)
                                   .ConfigureAwait(false))
                {
                    hover.ObserveLine(line);
                    foreach (var evt in _parser.ParseLines([line]))
                    {
                        await greChannel.Writer.WriteAsync(evt, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // stop
            }
            finally
            {
                greChannel.Writer.TryComplete();
            }
        }, ct);

        string? lastTurnLog = null;
        string? lastNoDecisionWhy = null;

        try
        {
            pending = engine.TryGetDecisionView();
            if (!await DrainPendingAsync(
                    () => pending,
                    v => pending = v,
                    engine,
                    busy,
                    options,
                    profile,
                    locator,
                    hover,
                    greChannel.Reader,
                    sequence,
                    ct).ConfigureAwait(false))
            {
                return;
            }

            if (engine.TryPromoteStickyPriority())
            {
                _reporter.OnInfo("promoted sticky MainPhase after keep/catch-up");
            }

            if (!await DrainPendingAsync(
                    () => pending,
                    v => pending = v,
                    engine,
                    busy,
                    options,
                    profile,
                    locator,
                    hover,
                    greChannel.Reader,
                    sequence,
                    ct).ConfigureAwait(false))
            {
                return;
            }

            if (pending is null && engine.TryGetDecisionView() is null)
            {
                var snap = engine.TryGetSnapshot();
                var turn = snap?.Turn;
                _reporter.OnInfo(
                    turn is null
                        ? "waiting for GRE (Main1 / your priority)… (tail live)"
                        : $"waiting for Main1… now turn={turn.TurnNumber} {turn.Phase}/{turn.Step}");
            }

            while (!ct.IsCancellationRequested)
            {
                GreEvent? evt = null;
                using (var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    pollCts.CancelAfter(TimeSpan.FromMilliseconds(400));
                    try
                    {
                        evt = await greChannel.Reader.ReadAsync(pollCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // heartbeat
                    }
                }

                if (evt is { } greEvent)
                {
                    sequence.Value++;
                    engine.Apply(greEvent with { Sequence = sequence.Value });

                    if (engine.TryGetSnapshot() is { } snap)
                    {
                        hover.SetMySeatId(snap.MySeatId);
                        NoteTurnBoundary(snap.Turn.TurnNumber);
                        var turnKey = $"{snap.Turn.TurnNumber}|{snap.Turn.Phase}|{snap.Turn.Step}";
                        if (!string.Equals(turnKey, lastTurnLog, StringComparison.Ordinal)
                            && pending is null
                            && !busy.IsBusy
                            && engine.TryGetDecisionView() is null)
                        {
                            lastTurnLog = turnKey;
                            _reporter.OnInfo(
                                $"GRE turn={snap.Turn.TurnNumber} {snap.Turn.Phase}/{snap.Turn.Step} (no decision yet)");
                        }
                    }
                }

                if (!TryResolvePendingLandAck(engine, options))
                {
                    return;
                }

                if (_requeueLandRetry)
                {
                    _requeueLandRetry = false;
                    pending ??= engine.TryGetDecisionView();
                    if (pending is not null)
                    {
                        _reporter.OnInfo("Re-queued Main1 for land drag retry");
                    }
                }

                TryScheduleKeepRetry(engine, busy, ref pending);

                // Main1 often arrives as turnInfo-only Diff; ensure sticky/actions open a decision.
                if (pending is null && !busy.IsBusy && _pendingLandAckId is null)
                {
                    var snap = engine.TryGetSnapshot();
                    if (snap is not null
                        && snap.Turn.TurnNumber > 0
                        && snap.Turn.Phase is "Phase_Main1" or "Phase_Main2"
                        && engine.TryGetDecisionView() is null)
                    {
                        if (engine.TryPromoteStickyPriority())
                        {
                            _reporter.OnInfo("promoted sticky MainPhase while waiting");
                        }

                        pending = engine.TryGetDecisionView();
                        if (pending is null)
                        {
                            var why = engine.DescribeWhyNoDecision();
                            if (!string.Equals(why, lastNoDecisionWhy, StringComparison.Ordinal))
                            {
                                lastNoDecisionWhy = why;
                                _reporter.OnInfo($"Main1 visible but no decision: {why}");
                            }
                        }
                    }
                    else
                    {
                        pending = engine.TryGetDecisionView();
                    }
                }

                if (!await DrainPendingAsync(
                        () => pending,
                        v => pending = v,
                        engine,
                        busy,
                        options,
                        profile,
                        locator,
                        hover,
                        greChannel.Reader,
                        sequence,
                        ct).ConfigureAwait(false))
                {
                    return;
                }

                if (greChannel.Reader.Completion.IsCompleted && greChannel.Reader.Count == 0)
                {
                    break;
                }
            }
        }
        finally
        {
            try
            {
                await tailTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected on Ctrl+C
            }
        }
    }

    private void NoteTurnBoundary(int turnNumber)
    {
        if (turnNumber <= 0 || turnNumber == _lastObservedTurnNumber)
        {
            return;
        }

        if (_lastObservedTurnNumber > 0)
        {
            _actuatedKeys.Clear();
            _landDragRetries = 0;
            _requeueLandRetry = false;
            if (_policy is FarmMvpPolicy farm)
            {
                // SyncTurn also clears on next Decide; force unsettled for the new turn now.
                farm.AllowLandRetry();
            }

            _reporter.OnInfo(
                $"Turn boundary {_lastObservedTurnNumber} → {turnNumber}: cleared actuate dedupe, land retry armed");
        }

        _lastObservedTurnNumber = turnNumber;
    }

    private GameView? TakeNewDecision(StateEngine engine)
    {
        return engine.TryGetDecisionView();
    }

    /// <returns>False = stop live loop.</returns>
    private bool TryResolvePendingLandAck(StateEngine engine, LiveExecuteOptions options)
    {
        if (_pendingLandAckId is not int landId || _pendingLandAckSince is not { } since)
        {
            return true;
        }

        var snap = engine.TryGetSnapshot();
        if (snap is not null && HandActionAck.IsConfirmed(snap, null, landId))
        {
            _reporter.OnInfo($"Land GRE-confirmed (id={landId} left hand)");
            if (_policy is FarmMvpPolicy farmOk)
            {
                farmOk.NotifyLandSettled();
            }

            _pendingLandAckId = null;
            _pendingLandAckSince = null;
            _pendingLandDecisionId = null;
            _landDragRetries = 0;
            return true;
        }

        if (DateTimeOffset.UtcNow - since < LandAckBudget)
        {
            return true;
        }

        // One longer retry before giving up — short drag often lifts the card but does not play it.
        if (_landDragRetries < 1
            && _pendingLandDecisionId is { } decisionId
            && snap is not null
            && snap.Turn.Phase is "Phase_Main1" or "Phase_Main2")
        {
            _landDragRetries++;
            _reporter.OnInfo(
                $"Land drag not confirmed (id={landId}) — retry {_landDragRetries}/1 with same Main1");
            _pendingLandAckId = null;
            _pendingLandAckSince = null;
            _actuatedKeys.Remove((decisionId, nameof(PlayLandIntent)));
            if (_policy is FarmMvpPolicy farm)
            {
                farm.AllowLandRetry();
            }

            _requeueLandRetry = true;
            return true;
        }

        _reporter.OnError(
            $"Land drag not confirmed by GRE (id={landId} still in hand / no zone change). Stopping.");
        _pendingLandAckId = null;
        _pendingLandAckSince = null;
        return options.Mode != FarmMvpMode.LandOnly;
    }

    private void TryScheduleKeepRetry(StateEngine engine, ActuatorBusy busy, ref GameView? pending)
    {
        if (_keepClickSentAt is not { } sentAt
            || pending is not null
            || busy.IsBusy)
        {
            return;
        }

        // Keep already accepted by client — do not click again while waiting for Main1.
        if (engine.MulliganResponseSeen)
        {
            _keepClickSentAt = null;
            return;
        }

        var snap = engine.TryGetSnapshot();
        if (snap is null)
        {
            return;
        }

        if (snap.Turn.TurnNumber > 0)
        {
            _keepClickSentAt = null;
            return;
        }

        if (DateTimeOffset.UtcNow - sentAt <= TimeSpan.FromSeconds(5))
        {
            return;
        }

        if (_keepClickAttempts >= 2)
        {
            return;
        }

        _reporter.OnInfo($"Keep not confirmed by GRE — retry Keep ({_keepClickAttempts + 1}/2)");
        engine.AllowMulliganRetry();
        _keepClickSentAt = null;
        pending = engine.TryGetDecisionView();
    }

    private async Task<bool> DrainPendingAsync(
        Func<GameView?> getPending,
        Action<GameView?> setPending,
        StateEngine engine,
        ActuatorBusy busy,
        LiveExecuteOptions options,
        CalibrationProfile profile,
        Win32WindowLocator locator,
        IHoverObjectIdSource hover,
        ChannelReader<GreEvent> greReader,
        SequenceHolder sequence,
        CancellationToken ct)
    {
        while (getPending() is { } view && !busy.IsBusy && !ct.IsCancellationRequested)
        {
            if (_pendingLandAckId is not null)
            {
                setPending(null);
                break;
            }

            setPending(null);
            if (!await ActuatePendingAsync(
                    view,
                    engine,
                    busy,
                    options,
                    profile,
                    locator,
                    hover,
                    greReader,
                    sequence,
                    ct).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    /// <returns>False = stop the live loop.</returns>
    private async Task<bool> ActuatePendingAsync(
        GameView view,
        StateEngine engine,
        ActuatorBusy busy,
        LiveExecuteOptions options,
        CalibrationProfile profile,
        Win32WindowLocator locator,
        IHoverObjectIdSource hover,
        ChannelReader<GreEvent> greReader,
        SequenceHolder sequence,
        CancellationToken ct)
    {
        var intent = _policy.Decide(view, _cards);

        // Wait-NoOps must re-evaluate when priority / Main1 arrives (no mouse, no dedupe burn).
        if (intent is NoOpIntent
            {
                Reason: FarmMvpPolicy.WaitingMain1PriorityReason
                or "waiting-main1-after-beginning"
                or "waiting-priority"
            })
        {
            _reporter.OnDecision(view, intent);
            return true;
        }

        // Hard guard: never click Pass on our Main1 while a land drop is still due.
        if (intent is PassPriorityIntent
            && _policy is FarmMvpPolicy farmPass
            && !farmPass.LandSettledThisTurn
            && PriorityWindow.IsOurTurnMain1(view.Board)
            && FarmMvpPolicy.HasPlayableLandInHand(view, view.Decision.LegalActions))
        {
            _reporter.OnInfo(
                "Refusing Pass on our Main1 — land still due " +
                $"(turn={view.Board.Turn.TurnNumber} prio={view.Board.Turn.PriorityPlayer}/{view.Board.Turn.ActivePlayer})");
            return true;
        }

        // Hard guard: never Pass through our Beginning/Draw (skips Main1 land window).
        if (intent is PassPriorityIntent
            && view.Board.Turn.Phase == "Phase_Beginning"
            && view.Board.MySeatId > 0
            && view.Board.Turn.ActivePlayer == view.Board.MySeatId)
        {
            _reporter.OnInfo(
                $"Refusing Pass during our Beginning ({view.Board.Turn.Step}) — waiting for Main1");
            return true;
        }

        var key = (view.Decision.DecisionId, intent.GetType().Name);
        if (!_actuatedKeys.Add(key))
        {
            if (intent is PlayLandIntent)
            {
                _reporter.OnInfo(
                    $"Skip duplicate PlayLand actuate (decisionId={view.Decision.DecisionId}) — already armed this id");
            }

            return true;
        }

        _reporter.OnDecision(view, intent);

        engine.ActuatorBusy = true;
        try
        {
            using (busy.Enter())
            {
                var current = engine.TryGetDecisionView() ?? view;
                if (!IntentPreflight.TryAccept(view, intent, current, out var rejectReason))
                {
                    if (intent is KeepHandIntent && view.Decision.Kind == DecisionKind.Mulligan)
                    {
                        engine.AcknowledgeMulliganAnswered();
                        rejectReason ??= "stale mulligan after keep";
                    }

                    if (intent is PlayLandIntent && _policy is FarmMvpPolicy farmStale)
                    {
                        // Actuate was armed then refused — reopen land for this turn.
                        farmStale.AllowLandRetry();
                        _actuatedKeys.Remove(key);
                    }

                    var stale = ActuateResult.FromKind(
                        ActuateOutcomeKind.StaleIntent,
                        intent.GetType().Name,
                        error: rejectReason,
                        targetInstanceId: IntentPreflight.GetHandTarget(intent));
                    _reporter.OnActuate(stale);
                    _reporter.OnAttempt(ActuateAttemptLog.From(view, intent, stale));
                    return true;
                }

                ActuateResult result;
                if (intent is PlayLandIntent play)
                {
                    if (!PriorityWindow.IsOurMain1(current.Board))
                    {
                        if (_policy is FarmMvpPolicy farmRefuse)
                        {
                            farmRefuse.AllowLandRetry();
                            _actuatedKeys.Remove(key);
                        }

                        result = ActuateResult.FromKind(
                            ActuateOutcomeKind.StaleIntent,
                            intent.GetType().Name,
                            error: "PlayLand refused: not our Main1",
                            targetInstanceId: play.InstanceId);
                    }
                    else
                    {
                        if (_policy is FarmMvpPolicy farmStart)
                        {
                            farmStart.NotifyLandActuateStarted();
                        }

                        _reporter.OnInfo(
                            $"Land actuate armed (id={play.InstanceId}) — scan only while our Main1");
                        result = await ExecutePlayLandWatchedAsync(
                            play,
                            options,
                            profile,
                            locator,
                            hover,
                            engine,
                            greReader,
                            sequence,
                            ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    result = await ExecuteIntentAsync(
                        intent,
                        options,
                        profile,
                        locator,
                        hover,
                        ct).ConfigureAwait(false);
                }

                if (intent is KeepHandIntent && result.Success)
                {
                    engine.AcknowledgeMulliganAnswered();
                    _keepClickSentAt = DateTimeOffset.UtcNow;
                    _keepClickAttempts++;
                    _reporter.OnInfo(
                        $"Keep click sent (attempt {_keepClickAttempts}) — waiting for MulliganResp / Main1");
                }

                if (intent is not KeepHandIntent)
                {
                    _keepClickSentAt = null;
                }

                if (intent is PlayLandIntent playOk && result.Kind == ActuateOutcomeKind.UiSucceeded)
                {
                    _pendingLandAckId = playOk.InstanceId;
                    _pendingLandAckSince = DateTimeOffset.UtcNow;
                    _pendingLandDecisionId = view.Decision.DecisionId;
                    _reporter.OnInfo(
                        $"Land drag sent (id={playOk.InstanceId}) — waiting GRE hand ack…");
                }

                _reporter.OnActuate(result);
                _reporter.OnAttempt(ActuateAttemptLog.From(current, intent, result));

                if (options.Mode == FarmMvpMode.LandOnly
                    && intent is PlayLandIntent
                    && result.Kind == ActuateOutcomeKind.HoverMiss
                    && result.Error?.Contains("cancelled: lost Main1", StringComparison.Ordinal) == true)
                {
                    _reporter.OnInfo("Land cancelled (priority lost) — will wait for next our Main1");
                    if (_policy is FarmMvpPolicy farm)
                    {
                        farm.AllowLandRetry();
                    }

                    _actuatedKeys.Remove(key);
                    return true;
                }

                if (options.Mode == FarmMvpMode.LandOnly
                    && intent is PlayLandIntent
                    && result.Kind == ActuateOutcomeKind.HoverMiss)
                {
                    _reporter.OnError(
                        "LandOnly: land miss — stopping live loop (no Pass cascade). " +
                        "Fix scan/hover, then re-run on a fresh Main1.");
                    return false;
                }

                return true;
            }
        }
        finally
        {
            engine.ActuatorBusy = false;
            engine.TryEmitAfterActuate();
        }
    }

    /// <summary>
    /// Run land scan/drag while pumping GRE; cancel immediately if we leave our Main1.
    /// </summary>
    private async Task<ActuateResult> ExecutePlayLandWatchedAsync(
        PlayLandIntent play,
        LiveExecuteOptions options,
        CalibrationProfile profile,
        Win32WindowLocator locator,
        IHoverObjectIdSource hover,
        StateEngine engine,
        ChannelReader<GreEvent> greReader,
        SequenceHolder sequence,
        CancellationToken ct)
    {
        using var landCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var execTask = ExecuteIntentAsync(play, options, profile, locator, hover, landCts.Token);

        while (!execTask.IsCompleted)
        {
            while (greReader.TryRead(out var evt))
            {
                sequence.Value++;
                engine.Apply(evt with { Sequence = sequence.Value });
                if (engine.TryGetSnapshot() is { } snap)
                {
                    if (hover is LogHoverObjectIdSource logHover)
                    {
                        logHover.SetMySeatId(snap.MySeatId);
                    }

                    if (!PriorityWindow.IsOurTurnMain1(snap)
                        || !snap.HandInstanceIds.Contains(play.InstanceId))
                    {
                        var turn = snap.Turn;
                        _reporter.OnInfo(
                            $"Cancel land scan/drag — left our Main1 " +
                            $"(turn={turn.TurnNumber} {turn.Phase} " +
                            $"prio={turn.PriorityPlayer}/{turn.ActivePlayer} me={snap.MySeatId})");
                        landCts.Cancel();
                        break;
                    }
                }
            }

            if (landCts.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.WhenAny(execTask, Task.Delay(25, ct)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                landCts.Cancel();
                break;
            }
        }

        try
        {
            return await execTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ActuateResult.FromKind(
                ActuateOutcomeKind.HoverMiss,
                nameof(PlayLandIntent),
                error: "cancelled: lost Main1 priority during land actuate",
                targetInstanceId: play.InstanceId);
        }
    }

    private static async Task<ActuateResult> ExecuteIntentAsync(
        Intent intent,
        LiveExecuteOptions options,
        CalibrationProfile profile,
        Win32WindowLocator locator,
        IHoverObjectIdSource hover,
        CancellationToken ct)
    {
        if (options.DryRun)
        {
            var dryMap = new CoordinateMap(
                locator.FindMtgaClientRect() ?? new WindowRect(0, 0, 1920, 1080),
                profile);
            var hoverId = IntentPreflight.GetHandTarget(intent) ?? 1;
            var dryExecutor = new IntentExecutor(
                dryMap,
                profile,
                new RecordingInputBackend(),
                new ImmediateHoverObjectIdSource(hoverId),
                hoverPointTimeout: TimeSpan.FromMilliseconds(1),
                hoverMoveDelay: TimeSpan.Zero);
            return await dryExecutor.ExecuteAsync(intent, ct).ConfigureAwait(false);
        }

        if (!locator.TryFocusMtga())
        {
            return ActuateResult.FromKind(
                ActuateOutcomeKind.Failed,
                intent.GetType().Name,
                error: "MTGA window not found / focus failed.",
                targetInstanceId: IntentPreflight.GetHandTarget(intent));
        }

        await Task.Delay(150, ct).ConfigureAwait(false);

        var rect = locator.FindMtgaClientRect();
        if (rect is null)
        {
            return ActuateResult.FromKind(
                ActuateOutcomeKind.Failed,
                intent.GetType().Name,
                error: "MTGA client rect not found.",
                targetInstanceId: IntentPreflight.GetHandTarget(intent));
        }

        var map = new CoordinateMap(rect.Value, profile);
        var executor = new IntentExecutor(
            map,
            profile,
            new SendInputBackend(),
            hover);
        return await executor.ExecuteAsync(intent, ct).ConfigureAwait(false);
    }
}
