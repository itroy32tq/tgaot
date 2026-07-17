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

    private readonly GreLogTailer _tailer;
    private readonly GreLogParser _parser;
    private readonly ILiveExecuteReporter _reporter;
    private readonly IPolicy _policy;
    private readonly ICardDatabase _cards;
    private readonly CardDatabaseResolver.ResolveResult _cardsMeta;

    public LiveExecuteRunner(ILiveExecuteReporter reporter, LiveExecuteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var resolved = CardDatabaseResolver.Resolve(options.CardsPath, options.CardsOverlayPath);
        _tailer = new GreLogTailer();
        _parser = new GreLogParser();
        _reporter = reporter;
        _policy = PolicyFactory.Create(options.PolicyName);
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
            if (!catchUp && !busy.IsBusy)
            {
                pending = view;
            }
        };

        // Rebuild state from the end of Player.log so prompts already on screen (mulligan)
        // are seen even when the process starts after MulliganReq was written.
        var recent = _tailer.ParseRecent(options.LogPath, CatchUpMaxBytes);
        ulong sequence = 0;
        foreach (var evt in recent.Events)
        {
            ct.ThrowIfCancellationRequested();
            sequence++;
            engine.Apply(evt with { Sequence = sequence });
        }

        if (engine.TryGetSnapshot() is { } catchUpSnap)
        {
            hover.SetMySeatId(catchUpSnap.MySeatId);
        }

        catchUp = false;
        _reporter.OnInfo(
            $"catch-up: {recent.Events.Count} GRE events from last {recent.BytesRead / 1024} KB");

        pending = engine.TryGetDecisionView();
        if (pending is not null)
        {
            await ActuatePendingAsync(
                pending,
                engine,
                busy,
                options,
                profile,
                locator,
                hover,
                ct).ConfigureAwait(false);
            pending = null;
        }
        else
        {
            _reporter.OnInfo("catch-up: no open decision — waiting for new GRE (enter match / take action).");
        }

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

        try
        {
            await foreach (var evt in greChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                sequence++;
                engine.Apply(evt with { Sequence = sequence });

                if (engine.TryGetSnapshot() is { } snap)
                {
                    hover.SetMySeatId(snap.MySeatId);
                }

                if (pending is null || busy.IsBusy)
                {
                    continue;
                }

                var view = pending;
                pending = null;
                await ActuatePendingAsync(
                    view,
                    engine,
                    busy,
                    options,
                    profile,
                    locator,
                    hover,
                    ct).ConfigureAwait(false);
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

    private async Task ActuatePendingAsync(
        GameView view,
        StateEngine engine,
        ActuatorBusy busy,
        LiveExecuteOptions options,
        CalibrationProfile profile,
        Win32WindowLocator locator,
        IHoverObjectIdSource hover,
        CancellationToken ct)
    {
        var intent = _policy.Decide(view, _cards);
        _reporter.OnDecision(view, intent);

        engine.ActuatorBusy = true;
        try
        {
            using (busy.Enter())
            {
                var result = await ExecuteIntentAsync(
                    intent,
                    options,
                    profile,
                    locator,
                    hover,
                    ct).ConfigureAwait(false);
                _reporter.OnActuate(result);
            }
        }
        finally
        {
            engine.ActuatorBusy = false;
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
            var dryExecutor = new IntentExecutor(
                dryMap,
                profile,
                new RecordingInputBackend(),
                new ImmediateHoverObjectIdSource(),
                hoverPointTimeout: TimeSpan.FromMilliseconds(1),
                hoverMoveDelay: TimeSpan.Zero);
            return await dryExecutor.ExecuteAsync(intent, ct).ConfigureAwait(false);
        }

        if (!locator.TryFocusMtga())
        {
            return new ActuateResult(false, intent.GetType().Name, [], "MTGA window not found / focus failed.");
        }

        var rect = locator.FindMtgaClientRect();
        if (rect is null)
        {
            return new ActuateResult(false, intent.GetType().Name, [], "MTGA client rect not found.");
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
