using System.Text.Json;
using MtgaBot.Ingest;

namespace MtgaBot.State.Tests;

public class MulliganStateEngineTests
{
    [Fact]
    public void OpeningBatch_GameStateThenMulliganReq_EmitsMulliganNotMainPhase()
    {
        var engine = new StateEngine();
        var views = new List<GameView>();
        engine.DecisionReady += views.Add;

        var gsm = ParseMessage(
            """
            {
              "type": "GREMessageType_GameStateMessage",
              "systemSeatIds": [1],
              "gameStateMessage": {
                "type": "GameStateType_Diff",
                "turnInfo": { "activePlayer": 2, "decisionPlayer": 1 },
                "zones": [
                  { "zoneId": 31, "type": "ZoneType_Hand", "ownerSeatId": 1, "objectInstanceIds": [163, 164] }
                ],
                "gameObjects": [
                  { "instanceId": 163, "grpId": 1, "zoneId": 31, "ownerSeatId": 1 },
                  { "instanceId": 164, "grpId": 2, "zoneId": 31, "ownerSeatId": 1 }
                ],
                "players": [
                  { "systemSeatNumber": 1, "lifeTotal": 20 },
                  { "systemSeatNumber": 2, "lifeTotal": 20 }
                ],
                "actions": [
                  { "seatId": 1, "action": { "actionType": "ActionType_Play", "instanceId": 163 } },
                  { "seatId": 1, "action": { "actionType": "ActionType_Cast", "instanceId": 164 } }
                ]
              }
            }
            """);

        var mulligan = ParseMessage(
            """
            {
              "type": "GREMessageType_MulliganReq",
              "systemSeatIds": [1],
              "mulliganReq": { "mulliganType": "MulliganType_London" }
            }
            """);

        engine.Apply(new GreEvent(1, null, "line", gsm, 0));
        engine.Apply(new GreEvent(2, null, "line", mulligan, 1));

        Assert.DoesNotContain(views, v => v.Decision.Kind == DecisionKind.MainPhase);
        Assert.Contains(views, v => v.Decision.Kind == DecisionKind.Mulligan);
        Assert.Equal(DecisionKind.Mulligan, views.Last().Decision.Kind);
    }

    private static GreMessage ParseMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.Clone();
        var type = root.GetProperty("type").GetString() ?? string.Empty;
        return new GreMessage(type, root, GreTrafficDirection.GreToClient);
    }
}
