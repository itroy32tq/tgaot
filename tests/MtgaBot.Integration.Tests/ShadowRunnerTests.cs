using MtgaBot.Decide;
using MtgaBot.Host.Shadow;
using MtgaBot.Ingest;
using MtgaBot.State;

namespace MtgaBot.Integration.Tests;

public sealed class CapturingShadowReporter : IShadowReporter
{
    public List<GameView> Decisions { get; } = [];

    public List<Intent> Intents { get; } = [];

    public ShadowOptions? StartedWith { get; private set; }

    public CardDatabaseResolver.ResolveResult? StartedCards { get; private set; }

    public ShadowRunResult? CompletedWith { get; private set; }

    public List<string> Errors { get; } = [];

    public void OnStarted(ShadowOptions options, CardDatabaseResolver.ResolveResult cards)
    {
        StartedWith = options;
        StartedCards = cards;
    }

    public void OnDecision(GameView view, Intent intent)
    {
        Decisions.Add(view);
        Intents.Add(intent);
    }

    public void OnReplayComplete(ShadowRunResult result) => CompletedWith = result;

    public void OnFollowStarted()
    {
    }

    public void OnGreEvent(int eventCount, string messageType)
    {
    }

    public void OnError(string message) => Errors.Add(message);
}

public class ShadowRunnerTests
{
    [Fact]
    public async Task ReplayFixture_EmitsDecisionsWithIntents()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "hand-select-log_tail.txt");
        Assert.True(File.Exists(path), $"fixture missing: {path}");

        var reporter = new CapturingShadowReporter();
        var runner = new ShadowRunner(reporter, new ShadowOptions(path, Follow: false, PolicyName: "FarmMvp"));
        var options = new ShadowOptions(path, Follow: false, PolicyName: "FarmMvp");

        var result = await runner.RunAsync(options, CancellationToken.None);

        Assert.True(result.EventCount > 0);
        Assert.True(result.DecisionCount > 0);
        Assert.Equal(result.DecisionCount, reporter.Decisions.Count);
        Assert.Equal(result.DecisionCount, reporter.Intents.Count);
        Assert.NotNull(reporter.CompletedWith);

        Assert.Contains(reporter.Decisions, view => view.Board.MyLife > 0);
        Assert.Contains(reporter.Decisions, view => view.Decision.LegalActions.Count > 0);
        Assert.Equal(MatchPhase.InMatch, reporter.Decisions.Last().Lifecycle);

        Assert.All(reporter.Intents.Zip(reporter.Decisions), pair =>
            Assert.True(IntentValidator.IsLegal(pair.First, pair.Second.Decision)));
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
        Assert.Equal("FarmMvp", options.PolicyName);
    }

    [Fact]
    public void Parse_Policy()
    {
        var options = ShadowArgs.Parse(["--log", @"C:\tmp\Player.log", "--policy", "Pass"]);

        Assert.Equal("Pass", options.PolicyName);
    }

    [Fact]
    public void Parse_CardsPath()
    {
        var options = ShadowArgs.Parse(
            ["--log", @"C:\tmp\Player.log", "--cards", @"D:\data\cards.json", "--cards-overlay", @"D:\data\starter.json"]);

        Assert.Equal(@"D:\data\cards.json", options.CardsPath);
        Assert.Equal(@"D:\data\starter.json", options.CardsOverlayPath);
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
        Assert.Throws<ArgumentException>(() => ShadowArgs.Parse(["--unknown"]));
    }

    private sealed class StubLocator(string path) : IPlayerLogLocator
    {
        public string GetDefaultPlayerLogPath() => path;
    }
}
