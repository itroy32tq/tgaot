using MtgaBot.Ingest;

namespace MtgaBot.Ingest.Tests;

public class GreMessageDeserializerTests
{
    [Fact]
    public void DeserializeLine_ClientToGre_ParsesClientMessages()
    {
        const string line =
            """
            { "timestamp": "2000", "clientToGreEvent": { "clientToGreMessages": [ { "type": "ClientMessageType_SelectTargetsResp", "systemSeatIds": [ 1 ] } ] } }
            """;

        var deserializer = new GreMessageDeserializer();
        var messages = deserializer.DeserializeLine(line);

        Assert.Single(messages);
        Assert.Equal("ClientMessageType_SelectTargetsResp", messages[0].Type);
        Assert.Equal(GreTrafficDirection.ClientToGre, messages[0].Direction);
    }

    [Fact]
    public void DeserializeLine_InvalidJson_ReturnsEmpty()
    {
        var deserializer = new GreMessageDeserializer();

        Assert.Empty(deserializer.DeserializeLine("{ this is not json"));
    }
}
