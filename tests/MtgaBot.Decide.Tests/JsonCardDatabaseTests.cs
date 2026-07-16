using System.Text;

namespace MtgaBot.Decide.Tests;

public class JsonCardDatabaseTests
{
    [Fact]
    public void Load_ArrayFormat_MapsFields()
    {
        const string json = """
                            [
                              {
                                "grpId": 1001,
                                "name": "Grizzly Bears",
                                "types": ["Creature"],
                                "manaCost": "{1}{G}",
                                "oracleText": "Trample"
                              },
                              {
                                "grpId": "1002",
                                "mana_cost": "{1}{R}",
                                "type_line": "Instant",
                                "oracle_text": "Deal 3 damage."
                              }
                            ]
                            """;

        var db = JsonCardDatabase.Load(ToStream(json));

        Assert.Equal(2, db.Count);
        Assert.True(db.TryGet(1001, out var bears));
        Assert.Equal("Grizzly Bears", bears.Name);
        Assert.True(bears.IsCreature);
        Assert.Equal("{1}{G}", bears.ManaCost);
        Assert.Equal("Trample", bears.OracleText);

        Assert.True(db.TryGet(1002, out var strike));
        Assert.Equal("Card#1002", strike.Name);
        Assert.True(strike.IsInstant);
        Assert.Equal("{1}{R}", strike.ManaCost);
        Assert.Equal("Deal 3 damage.", strike.OracleText);
    }

    [Fact]
    public void Load_ObjectMap_UsesKeyAsGrpIdFallback()
    {
        const string json = """
                            {
                              "93756": {
                                "name": "Inspiration from Beyond",
                                "types": ["Sorcery"],
                                "manaCost": "{2}{U}",
                                "oracleText": "Return a card from your graveyard."
                              }
                            }
                            """;

        var db = JsonCardDatabase.Load(ToStream(json));

        Assert.True(db.TryGet(93756, out var card));
        Assert.Equal("Inspiration from Beyond", card.Name);
        Assert.True(card.IsSorcery);
    }

    [Fact]
    public void WithOverlay_WinsOnCollision()
    {
        var baseDb = JsonCardDatabase.Load(ToStream(
            """[{"grpId":1,"types":["Creature"],"manaCost":"{1}{G}"}]"""));
        var overlay = JsonCardDatabase.Load(ToStream(
            """{"1":{"grpId":1,"name":"Bear","types":["Creature"],"manaCost":"{1}{G}","oracleText":"scry 1"}}"""));

        var merged = baseDb.WithOverlay(overlay);

        Assert.True(merged.TryGet(1, out var card));
        Assert.Equal("Bear", card.Name);
        Assert.Equal("scry 1", card.OracleText);
    }

    [Fact]
    public void Load_FromTempFile_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mtgabot-cards-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """[{"grpId":42,"name":"Forest","types":["Basic","Land"],"manaCost":""}]""",
                Encoding.UTF8);

            var db = JsonCardDatabase.Load(path);

            Assert.True(db.TryGet(42, out var forest));
            Assert.True(forest.IsLand);
            Assert.Equal("Forest", forest.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_FileWithOverlay_Merges()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mtgabot-cards-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var cards = Path.Combine(dir, "cards.json");
        var overlay = Path.Combine(dir, "starter.json");
        try
        {
            File.WriteAllText(cards, """[{"grpId":1,"types":["Creature"],"manaCost":"{G}"}]""");
            File.WriteAllText(
                overlay,
                """{"1":{"grpId":1,"name":"Elf","types":["Creature"],"manaCost":"{G}","oracleText":""}}""");

            var db = JsonCardDatabase.Load(cards, overlay);

            Assert.True(db.TryGet(1, out var elf));
            Assert.Equal("Elf", elf.Name);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FarmMvp_UsesLoadedTypesToCastCreature()
    {
        var db = JsonCardDatabase.Load(ToStream(
            """
            [{"grpId":1001,"name":"Grizzly Bears","types":["Creature"],"manaCost":"{1}{G}","oracleText":""}]
            """));
        var policy = new FarmMvpPolicy();
        var view = new State.GameView(
            new State.GameSnapshot(
                MySeatId: 1,
                Turn: new State.TurnInfo("Phase_Main1", "Step_Begin", 3, 1, 1, 1),
                Objects: new Dictionary<int, State.CardView>
                {
                    [11] = new(11, 1001, 1, "ZoneType_Hand", 1, false),
                },
                HandInstanceIds: [11],
                BattlefieldInstanceIds: [],
                StackInstanceIds: [],
                MyLife: 20,
                OpponentLife: 20,
                Mana: State.ManaPool.Empty,
                PendingMessageCount: 0),
            new State.DecisionPoint(
                1,
                State.DecisionKind.MainPhase,
                1,
                [
                    new State.LegalAction("ActionType_Cast", 11, 1, null),
                    new State.LegalAction("ActionType_Pass", null, 1, null),
                ],
                null),
            State.MatchPhase.InMatch);

        Assert.Equal(new CastIntent(11), policy.Decide(view, db));
    }

    private static MemoryStream ToStream(string json) =>
        new(Encoding.UTF8.GetBytes(json));
}

public class CardDatabaseResolverTests
{
    [Fact]
    public void Resolve_ExplicitMissing_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            CardDatabaseResolver.Resolve(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.json")));
    }

    [Fact]
    public void Resolve_ExplicitPath_Loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mtgabot-resolve-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """[{"grpId":7,"types":["Land"]}]""");
            var result = CardDatabaseResolver.Resolve(path);

            Assert.Equal(1, result.Count);
            Assert.Equal(Path.GetFullPath(path), result.CardsPath);
            Assert.True(result.Database.TryGet(7, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
