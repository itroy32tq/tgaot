using MtgaBot.Ingest;
using MtgaBot.State;

namespace MtgaBot.State.Tests;

public class StateEngineTests
{
    [Fact]
    public void ReplayGoldenLog_ProducesHandAndOpenDecisions()
    {
        var tailer = new GreLogTailer();
        var engine = new StateEngine();
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "hand-select-log_tail.txt");
        var events = tailer.ParseFile(path);

        var decisions = new List<GameView>();
        engine.DecisionReady += view => decisions.Add(view);

        foreach (var evt in events)
        {
            engine.Apply(evt);
        }

        var snapshot = engine.TryGetSnapshot();
        Assert.NotNull(snapshot);
        Assert.True(snapshot.HandInstanceIds.Count > 0);

        Assert.NotEmpty(decisions);
        Assert.Contains(decisions, view => view.Board.MyLife > 0);
        Assert.Equal(MatchPhase.InMatch, decisions.Last().Lifecycle);
    }
}
