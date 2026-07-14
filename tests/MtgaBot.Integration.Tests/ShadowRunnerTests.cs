using MtgaBot.Host.Shadow;
using MtgaBot.Ingest;
using MtgaBot.State;

namespace MtgaBot.Integration.Tests;

public sealed class CapturingShadowReporter : IShadowReporter
{
    public List<GameView> Decisions { get; } = [];

    public ShadowOptions? StartedWith { get; private set; }

    public ShadowRunResult? CompletedWith { get; private set; }

    public List<string> Errors { get; } = [];

    public void OnStarted(ShadowOptions options) => StartedWith = options;

    public void OnDecision(GameView view) => Decisions.Add(view);

    public void OnReplayComplete(ShadowRunResult result) => CompletedWith = result;

    public void OnFollowStarted()
    {
    }

    public void OnError(string message) => Errors.Add(message);
}

public class ShadowRunnerTests
{
    [Fact]
    public async Task ReplayFixture_EmitsDecisionsWithHandAndLife()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "hand-select-log_tail.txt");
        Assert.True(File.Exists(path), $"fixture missing: {path}");

        var reporter = new CapturingShadowReporter();
        var runner = new ShadowRunner(reporter);
        var options = new ShadowOptions(path, Follow: false);

        var result = await runner.RunAsync(options, CancellationToken.None);

        Assert.True(result.EventCount > 0);
        Assert.True(result.DecisionCount > 0);
        Assert.Equal(result.DecisionCount, reporter.Decisions.Count);
        Assert.NotNull(reporter.CompletedWith);

        Assert.Contains(reporter.Decisions, view => view.Board.HandInstanceIds.Count > 0);
        Assert.Contains(reporter.Decisions, view => view.Board.MyLife > 0);
        Assert.Contains(reporter.Decisions, view => view.Decision.LegalActions.Count > 0);
    }

    [Fact]
    public void FormatDecision_IncludesTurnLegalAndLife()
    {
        var view = new GameView(
            new GameSnapshot(
                MySeatId: 1,
                Turn: new TurnInfo("Phase_Main1", "Step_Begin", 4, 1, 1, 1),
                Objects: new Dictionary<int, CardView>(),
                HandInstanceIds: [10, 11],
                BattlefieldInstanceIds: [20],
                StackInstanceIds: [],
                MyLife: 20,
                OpponentLife: 18,
                Mana: new ManaPool(0, 0, 0, 0, 0, 0),
                PendingMessageCount: 0),
            new DecisionPoint(
                DecisionId: 99,
                Kind: DecisionKind.MainPhase,
                SystemSeatId: 1,
                LegalActions:
                [
                    new LegalAction("ActionType_Cast", 123, 1, null),
                    new LegalAction("ActionType_Pass", null, 1, null),
                ],
                Prompt: null),
            MatchPhase.InMatch);

        var text = ShadowConsoleReporter.FormatDecision(view);

        Assert.Contains("[turn 4 Main1]", text);
        Assert.Contains("decision=99", text);
        Assert.Contains("life 20/18", text);
        Assert.Contains("hand=2", text);
        Assert.Contains("Cast(123)", text);
        Assert.Contains("Pass", text);
    }
}

public class ShadowArgsTests
{
    [Fact]
    public void Parse_LogAndFollow()
    {
        var options = ShadowArgs.Parse(["--log", @"C:\tmp\Player.log", "--follow"]);

        Assert.Equal(@"C:\tmp\Player.log", options.LogPath);
        Assert.True(options.Follow);
    }

    [Fact]
    public void Parse_DefaultLogPath_WhenOmitted()
    {
        var locator = new StubLocator(@"D:\logs\Player.log");
        var options = ShadowArgs.Parse(["--follow"], locator);

        Assert.Equal(@"D:\logs\Player.log", options.LogPath);
        Assert.True(options.Follow);
    }

    [Fact]
    public void Parse_UnknownArg_Throws()
    {
        Assert.Throws<ArgumentException>(() => ShadowArgs.Parse(["--policy", "FarmMvp"]));
    }

    private sealed class StubLocator(string path) : IPlayerLogLocator
    {
        public string GetDefaultPlayerLogPath() => path;
    }
}
