using System.Text.Json;
using MtgaBot.Ingest;
using MtgaBot.State;

namespace MtgaBot.State.Tests;

public class GameStateReducerTests
{
    [Fact]
    public void ApplyGameStateMessage_Diff_MergesHandAndActions()
    {
        var reducer = new GameStateReducer();
        using var document = JsonDocument.Parse(File.ReadAllText(FixturePath("game-state-diff.json")));

        reducer.ApplyGameStateMessage(document.RootElement);

        Assert.Equal(2, reducer.State.GameObjects.Count);
        Assert.Single(reducer.State.Actions);
        Assert.Equal(0, reducer.State.PendingMessageCount);
        Assert.Equal("Phase_Main1", reducer.State.Turn.Phase);
    }

    [Fact]
    public void ApplyGameStateMessage_Diff_DeletesRemovedInstances()
    {
        var reducer = new GameStateReducer();
        const string initial = """
            {
              "type": "GameStateType_Full",
              "gameObjects": [
                { "instanceId": 1, "grpId": 10, "zoneId": 1, "ownerSeatId": 1 },
                { "instanceId": 2, "grpId": 20, "zoneId": 1, "ownerSeatId": 1 }
              ],
              "zones": []
            }
            """;
        const string diff = """
            {
              "type": "GameStateType_Diff",
              "diffDeletedInstanceIds": [2],
              "zones": []
            }
            """;

        using var initialDoc = JsonDocument.Parse(initial);
        using var diffDoc = JsonDocument.Parse(diff);

        reducer.ApplyGameStateMessage(initialDoc.RootElement);
        reducer.ApplyGameStateMessage(diffDoc.RootElement);

        Assert.Single(reducer.State.GameObjects);
        Assert.Equal(1, reducer.State.GameObjects[0]["instanceId"].GetInt32());
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);
}
