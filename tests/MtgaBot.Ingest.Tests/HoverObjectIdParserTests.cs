using MtgaBot.Ingest;

namespace MtgaBot.Ingest.Tests;

public class HoverObjectIdParserTests
{
    [Fact]
    public void Parse_UiHoverMessage()
    {
        const string line = """
                            {"greToClientEvent":{"greToClientMessages":[{"uiMessage":{"seatIds":[1],"hover":{"objectId":160}}}]}}
                            """;

        Assert.Equal(160, HoverObjectIdParser.TryParse(line, mySeatId: 1));
    }

    [Fact]
    public void Parse_UiHover_FiltersOtherSeat()
    {
        const string line = """
                            {"greToClientEvent":{"greToClientMessages":[{"uiMessage":{"seatIds":[2],"hover":{"objectId":160}}}]}}
                            """;

        Assert.Null(HoverObjectIdParser.TryParse(line, mySeatId: 1));
    }

    [Fact]
    public void Parse_FragmentRegex()
    {
        Assert.Equal(42, HoverObjectIdParser.TryParse("""noise "objectId": 42 trailing"""));
    }

    [Fact]
    public void Parse_IgnoresGameStatePayload()
    {
        const string line = """
                            {"greToClientEvent":{"greToClientMessages":[{"type":"GREMessageType_GameStateMessage","gameStateMessage":{"gameObjects":[{"objectId":99}]}}]}}
                            """;

        Assert.Null(HoverObjectIdParser.TryParse(line));
    }
}
