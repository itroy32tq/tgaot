using MtgaBot.Ingest;
using MtgaBot.State;

namespace MtgaBot.Host.Shadow;

public sealed class ShadowRunner
{
    private readonly GreLogTailer _tailer;
    private readonly IShadowReporter _reporter;

    public ShadowRunner(IShadowReporter reporter)
        : this(new GreLogTailer(), reporter)
    {
    }

    public ShadowRunner(GreLogTailer tailer, IShadowReporter reporter)
    {
        _tailer = tailer;
        _reporter = reporter;
    }

    public async Task<ShadowRunResult> RunAsync(ShadowOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LogPath);

        if (!File.Exists(options.LogPath))
        {
            _reporter.OnError($"Player.log not found: {options.LogPath}");
            throw new FileNotFoundException("Player.log not found.", options.LogPath);
        }

        _reporter.OnStarted(options);

        var engine = new StateEngine();
        var decisionCount = 0;
        engine.DecisionReady += view =>
        {
            decisionCount++;
            _reporter.OnDecision(view);
        };

        if (options.Follow)
        {
            _reporter.OnFollowStarted();
            var eventCount = 0;
            await foreach (var evt in _tailer.TailLive(options.LogPath, ct))
            {
                eventCount++;
                engine.Apply(evt);
            }

            return new ShadowRunResult(eventCount, decisionCount, Followed: true);
        }

        var events = _tailer.ParseFile(options.LogPath);
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            engine.Apply(evt);
        }

        var result = new ShadowRunResult(events.Count, decisionCount, Followed: false);
        _reporter.OnReplayComplete(result);
        return result;
    }
}
