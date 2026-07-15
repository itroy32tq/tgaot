using System.Text.Json;
using MtgaBot.State;
using MtgaBot.State.Internal;

namespace MtgaBot.State.Tests;

public class ZoneIndexTests
{
    [Fact]
    public void GetInstanceIds_PrefersGameObjectZoneIdOverStaleZoneArray()
    {
        var state = new MutableGameState();
        state.Zones.Add(ParseObject(
            """{ "zoneId": 31, "type": "ZoneType_Hand", "ownerSeatId": 1, "objectInstanceIds": [10, 20] }"""));
        state.Zones.Add(ParseObject(
            """{ "zoneId": 28, "type": "ZoneType_Battlefield", "objectInstanceIds": [] }"""));
        // Card 10 moved to battlefield in gameObjects, but zone array still lists it in hand.
        state.GameObjects.Add(ParseObject(
            """{ "instanceId": 10, "grpId": 1, "zoneId": 28, "ownerSeatId": 1 }"""));
        state.GameObjects.Add(ParseObject(
            """{ "instanceId": 20, "grpId": 2, "zoneId": 31, "ownerSeatId": 1 }"""));

        var index = new ZoneIndex();
        var hand = index.GetInstanceIds(state, "ZoneType_Hand", 1);
        var battlefield = index.GetInstanceIds(state, "ZoneType_Battlefield", 1);

        Assert.Equal([20], hand);
        Assert.Equal([10], battlefield);
    }

    private static Dictionary<string, JsonElement> ParseObject(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonMergeHelper.ParseObject(doc.RootElement);
    }
}
