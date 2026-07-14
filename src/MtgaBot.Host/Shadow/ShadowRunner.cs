using MtgaBot.Ingest;
using MtgaBot.State;

namespace MtgaBot.Host.Shadow;

public sealed class ShadowRunner(GreLogTailer tailer, IShadowReporter reporter)
{
    public ShadowRunner(IShadowReporter reporter)
        : this(new GreLogTailer(), reporter)
    {
    }

    public async Task<ShadowRunResult> RunAsync(ShadowOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LogPath);

        if (!File.Exists(options.LogPath))
        {
            reporter.OnError($"Player.log not found: {options.LogPath}");
            throw new FileNotFoundException("Player.log not found.", options.LogPath);
        }

        reporter.OnStarted(options);

        var engine = new StateEngine();
        var decisionCount = 0;
        engine.DecisionReady += view =>
        {
            decisionCount++;
            reporter.OnDecision(view);
        };

        if (options.Follow)
        {
            reporter.OnFollowStarted();
            var eventCount = 0;
            await foreach (var evt in tailer.TailLive(options.LogPath, ct))
            {
                eventCount++;
                reporter.OnGreEvent(eventCount, evt.Message.Type);
                engine.Apply(evt);
            }

            return new ShadowRunResult(eventCount, decisionCount, Followed: true);
        }

        var events = tailer.ParseFile(options.LogPath);
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            engine.Apply(evt);
        }

        var result = new ShadowRunResult(events.Count, decisionCount, Followed: false);
        reporter.OnReplayComplete(result);
        return result;
    }
}
