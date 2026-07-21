using MtgaBot.Decide;
using MtgaBot.Ingest;
using MtgaBot.State;

namespace MtgaBot.Host.Shadow;

public sealed class ShadowRunner
{
    private readonly GreLogTailer _tailer;
    private readonly IShadowReporter _reporter;
    private readonly IPolicy _policy;
    private readonly ICardDatabase _cards;
    private readonly CardDatabaseResolver.ResolveResult _cardsMeta;

    public ShadowRunner(
        GreLogTailer tailer,
        IShadowReporter reporter,
        IPolicy policy,
        ICardDatabase cards,
        CardDatabaseResolver.ResolveResult? cardsMeta = null)
    {
        _tailer = tailer;
        _reporter = reporter;
        _policy = policy;
        _cards = cards;
        _cardsMeta = cardsMeta ?? new CardDatabaseResolver.ResolveResult(
            cards,
            CardsPath: null,
            OverlayPath: null,
            Count: cards is JsonCardDatabase json ? json.Count : 0);
    }

    public ShadowRunner(IShadowReporter reporter, ShadowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var resolved = CardDatabaseResolver.Resolve(options.CardsPath, options.CardsOverlayPath);
        _tailer = new GreLogTailer();
        _reporter = reporter;
        _policy = PolicyFactory.Create(options.PolicyName, options.Mode);
        _cards = resolved.Database;
        _cardsMeta = resolved;
    }

    public ShadowRunner(IShadowReporter reporter)
        : this(new GreLogTailer(), reporter, new FarmMvpPolicy(), EmptyCardDatabase.Instance)
    {
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

        _reporter.OnStarted(options, _cardsMeta);

        var engine = new StateEngine();
        var decisionCount = 0;
        engine.DecisionReady += view =>
        {
            decisionCount++;
            var intent = _policy.Decide(view, _cards);
            _reporter.OnDecision(view, intent);
        };

        if (options.Follow)
        {
            _reporter.OnFollowStarted();
            var eventCount = 0;
            await foreach (var evt in _tailer.TailLive(options.LogPath, ct))
            {
                eventCount++;
                _reporter.OnGreEvent(eventCount, evt.Message.Type);
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
