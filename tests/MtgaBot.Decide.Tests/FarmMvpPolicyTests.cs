using MtgaBot.State;

namespace MtgaBot.Decide.Tests;

public class FarmMvpPolicyTests
{
    private static readonly ICardDatabase Cards = new InMemoryCardDatabase(
    [
        new CardInfo(1001, "Grizzly Bears", ["Creature"], "{1}{G}", "Trample"),
        new CardInfo(1002, "Lightning Strike", ["Instant"], "{1}{R}", "Lightning Strike deals 3 damage to any target."),
        new CardInfo(1003, "Scry Creature", ["Creature"], "{2}{U}", "When ~ enters, scry 1."),
        new CardInfo(1004, "Forest", ["Land", "Basic"], "", "Add {G}."),
    ]);

    [Fact]
    public void MainPhase_OnBeginning_PassesInsteadOfCasting()
    {
        var policy = new FarmMvpPolicy();
        var view = new GameView(
            new GameSnapshot(
                1,
                new TurnInfo("Phase_Beginning", "Step_Upkeep", 1, 1, 1, 1),
                new Dictionary<int, CardView>
                {
                    [11] = new(11, 1001, 1, "ZoneType_Hand", 1, false),
                },
                [11],
                [],
                [],
                20,
                20,
                ManaPool.Empty,
                0),
            new DecisionPoint(
                1,
                DecisionKind.MainPhase,
                1,
                [
                    new LegalAction("ActionType_Cast", 11, 1, null),
                    new LegalAction("ActionType_Play", 10, 1, null),
                ],
                null),
            MatchPhase.InMatch);

        Assert.IsType<PassPriorityIntent>(policy.Decide(view, Cards));
    }

    [Fact]
    public void MainPhase_PlaysLandOncePerTurn()
    {
        var policy = new FarmMvpPolicy();
        var view = MainView(
            turn: 3,
            actions:
            [
                new LegalAction("ActionType_Play", 10, 1, null),
                new LegalAction("ActionType_Cast", 11, 1, null),
                new LegalAction("ActionType_Pass", null, 1, null),
            ],
            objects: new Dictionary<int, CardView>
            {
                [10] = new(10, 1004, 1, "ZoneType_Hand", 1, false),
                [11] = new(11, 1001, 1, "ZoneType_Hand", 1, false),
            });

        var first = policy.Decide(view, Cards);
        var second = policy.Decide(view, Cards);

        Assert.Equal(new PlayLandIntent(10), first);
        Assert.Equal(new CastIntent(11), second);
        Assert.True(IntentValidator.IsLegal(first, view.Decision));
        Assert.True(IntentValidator.IsLegal(second, view.Decision));
    }

    [Fact]
    public void LandOnly_PlaysLandThenPasses_NoCast_NoAttack()
    {
        var policy = new FarmMvpPolicy(mode: FarmMvpMode.LandOnly);
        var main = MainView(
            turn: 3,
            actions:
            [
                new LegalAction("ActionType_Play", 10, 1, null),
                new LegalAction("ActionType_Cast", 11, 1, null),
                new LegalAction("ActionType_Pass", null, 1, null),
            ],
            objects: new Dictionary<int, CardView>
            {
                [10] = new(10, 1004, 1, "ZoneType_Hand", 1, false),
                [11] = new(11, 1001, 1, "ZoneType_Hand", 1, false),
            });

        Assert.Equal(new PlayLandIntent(10), policy.Decide(main, Cards));
        Assert.IsType<PassPriorityIntent>(policy.Decide(main, Cards));

        var attackers = new GameView(
            Board(turn: 3),
            new DecisionPoint(1, DecisionKind.Attackers, 1, [new LegalAction("Prompt", null, 1, null)], null),
            MatchPhase.InMatch);
        Assert.IsType<PassPriorityIntent>(policy.Decide(attackers, Cards));
    }

    [Fact]
    public void LandAndCast_CastsButDoesNotAttack()
    {
        var policy = new FarmMvpPolicy(mode: FarmMvpMode.LandAndCast);
        var main = MainView(
            turn: 3,
            actions:
            [
                new LegalAction("ActionType_Cast", 11, 1, null),
                new LegalAction("ActionType_Pass", null, 1, null),
            ],
            objects: new Dictionary<int, CardView>
            {
                [11] = new(11, 1001, 1, "ZoneType_Hand", 1, false),
            });

        Assert.Equal(new CastIntent(11), policy.Decide(main, Cards));

        var attackers = new GameView(
            Board(turn: 3),
            new DecisionPoint(1, DecisionKind.Attackers, 1, [new LegalAction("Prompt", null, 1, null)], null),
            MatchPhase.InMatch);
        Assert.IsType<PassPriorityIntent>(policy.Decide(attackers, Cards));
    }

    [Fact]
    public void MainPhase_SkipsInstantAndChooserCreature()
    {
        var policy = new FarmMvpPolicy();
        var view = MainView(
            turn: 1,
            actions:
            [
                new LegalAction("ActionType_Cast", 20, 1, null),
                new LegalAction("ActionType_Cast", 21, 1, null),
                new LegalAction("ActionType_Pass", null, 1, null),
            ],
            objects: new Dictionary<int, CardView>
            {
                [20] = new(20, 1002, 1, "ZoneType_Hand", 1, false),
                [21] = new(21, 1003, 1, "ZoneType_Hand", 1, false),
            });

        var intent = policy.Decide(view, Cards);

        Assert.IsType<PassPriorityIntent>(intent);
    }

    [Fact]
    public void MainPhase_WithoutCardDb_PlaysLandThenPasses()
    {
        var policy = new FarmMvpPolicy();
        var view = MainView(
            turn: 2,
            actions:
            [
                new LegalAction("ActionType_Play", 10, 1, null),
                new LegalAction("ActionType_Cast", 11, 1, null),
                new LegalAction("ActionType_Pass", null, 1, null),
            ],
            objects: new Dictionary<int, CardView>
            {
                [10] = new(10, 1004, 1, "ZoneType_Hand", 1, false),
                [11] = new(11, 1001, 1, "ZoneType_Hand", 1, false),
            });

        Assert.Equal(new PlayLandIntent(10), policy.Decide(view, EmptyCardDatabase.Instance));
        Assert.IsType<PassPriorityIntent>(policy.Decide(view, EmptyCardDatabase.Instance));
    }

    [Fact]
    public void Attackers_ReturnsAttackAll()
    {
        var policy = new FarmMvpPolicy();
        var view = new GameView(
            Board(turn: 4),
            new DecisionPoint(1, DecisionKind.Attackers, 1, [new LegalAction("Prompt", null, 1, null)], null),
            MatchPhase.InMatch);

        Assert.IsType<AttackAllIntent>(policy.Decide(view, Cards));
    }

    [Fact]
    public void Mulligan_KeepsHand()
    {
        var policy = new FarmMvpPolicy();
        var view = new GameView(
            Board(turn: 0),
            new DecisionPoint(1, DecisionKind.Mulligan, 1, [new LegalAction("Prompt", null, 1, null)], null),
            MatchPhase.InMatch);

        Assert.Equal(new KeepHandIntent(true), policy.Decide(view, Cards));
    }

    [Fact]
    public void PrefersHigherCmcSafeCreature()
    {
        var cards = new InMemoryCardDatabase(
        [
            new CardInfo(1, "Bear", ["Creature"], "{1}{G}", ""),
            new CardInfo(2, "Elephant", ["Creature"], "{3}{G}", ""),
        ]);
        var policy = new FarmMvpPolicy();
        var view = MainView(
            turn: 5,
            actions:
            [
                new LegalAction("ActionType_Cast", 30, 1, null),
                new LegalAction("ActionType_Cast", 31, 1, null),
            ],
            objects: new Dictionary<int, CardView>
            {
                [30] = new(30, 1, 1, "ZoneType_Hand", 1, false),
                [31] = new(31, 2, 1, "ZoneType_Hand", 1, false),
            });

        Assert.Equal(new CastIntent(31), policy.Decide(view, cards));
    }

    private static GameView MainView(int turn, IReadOnlyList<LegalAction> actions, IReadOnlyDictionary<int, CardView> objects) =>
        new(
            Board(turn, objects),
            new DecisionPoint(42, DecisionKind.MainPhase, 1, actions, null),
            MatchPhase.InMatch);

    private static GameSnapshot Board(int turn, IReadOnlyDictionary<int, CardView>? objects = null) =>
        new(
            MySeatId: 1,
            Turn: new TurnInfo("Phase_Main1", "Step_Begin", turn, 1, 1, 1),
            Objects: objects ?? new Dictionary<int, CardView>(),
            HandInstanceIds: objects?.Keys.ToList() ?? [],
            BattlefieldInstanceIds: [],
            StackInstanceIds: [],
            MyLife: 20,
            OpponentLife: 20,
            Mana: ManaPool.Empty,
            PendingMessageCount: 0);
}

public class CardPolicyTests
{
    [Fact]
    public void UnsupportedGrpId_IsBlocked()
    {
        var policy = new CardPolicy();
        Assert.True(policy.IsUnsupportedToCast(93756, null));
    }

    [Fact]
    public void CreatureWithScry_IsNotSafe()
    {
        var policy = new CardPolicy();
        var card = new CardInfo(1, "X", ["Creature"], "{1}{U}", "When ~ enters, scry 2.");

        Assert.False(policy.IsSafePermanentToCast(card));
    }

    [Fact]
    public void PlainCreature_IsSafe()
    {
        var policy = new CardPolicy();
        var card = new CardInfo(1, "Bear", ["Creature"], "{1}{G}", "");

        Assert.True(policy.IsSafePermanentToCast(card));
    }
}

public class PolicyFactoryTests
{
    [Fact]
    public void Create_FarmMvp_And_Pass()
    {
        Assert.IsType<FarmMvpPolicy>(PolicyFactory.Create("FarmMvp"));
        Assert.IsType<PassPolicy>(PolicyFactory.Create("Pass"));
        Assert.IsType<FarmMvpPolicy>(PolicyFactory.Create(null));
    }

    [Fact]
    public void Create_FarmMvp_WithMode()
    {
        var policy = Assert.IsType<FarmMvpPolicy>(PolicyFactory.Create("FarmMvp", FarmMvpMode.LandOnly));
        Assert.Equal(FarmMvpMode.LandOnly, policy.Mode);
    }

    [Fact]
    public void ParseMode_Aliases()
    {
        Assert.Equal(FarmMvpMode.LandOnly, PolicyFactory.ParseMode("land-only"));
        Assert.Equal(FarmMvpMode.LandAndCast, PolicyFactory.ParseMode("LandAndCast"));
        Assert.Equal(FarmMvpMode.FullMvp, PolicyFactory.ParseMode(null));
    }
}
