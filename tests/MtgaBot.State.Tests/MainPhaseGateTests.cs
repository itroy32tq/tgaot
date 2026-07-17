using System.Text.Json;
using MtgaBot.Ingest;

namespace MtgaBot.State.Tests;

public class MainPhaseGateTests
{
    [Fact]
    public void BeginningPhase_WithStickyActions_DoesNotEmitMainPhase()
    {
        var engine = new StateEngine();
        var views = new List<GameView>();
        engine.DecisionReady += views.Add;

        // Seat + hand after keep, still in Beginning (upkeep/draw) with sticky Cast/Play.
        engine.Apply(Evt(1, """
            {
              "type": "GREMessageType_GameStateMessage",
              "systemSeatIds": [1],
              "gameStateMessage": {
                "type": "GameStateType_Diff",
                "turnInfo": {
                  "phase": "Phase_Beginning",
                  "step": "Step_Upkeep",
                  "turnNumber": 1,
                  "activePlayer": 1,
                  "priorityPlayer": 1,
                  "decisionPlayer": 1
                },
                "zones": [
                  { "zoneId": 31, "type": "ZoneType_Hand", "ownerSeatId": 1, "objectInstanceIds": [159, 160, 161] }
                ],
                "gameObjects": [
                  { "instanceId": 159, "grpId": 1, "zoneId": 31, "ownerSeatId": 1 },
                  { "instanceId": 160, "grpId": 2, "zoneId": 31, "ownerSeatId": 1 },
                  { "instanceId": 161, "grpId": 3, "zoneId": 31, "ownerSeatId": 1 }
                ],
                "players": [
                  { "systemSeatNumber": 1, "lifeTotal": 20 },
                  { "systemSeatNumber": 2, "lifeTotal": 20 }
                ],
                "actions": [
                  { "seatId": 1, "action": { "actionType": "ActionType_Cast", "instanceId": 159 } },
                  { "seatId": 1, "action": { "actionType": "ActionType_Play", "instanceId": 160 } },
                  { "seatId": 1, "action": { "actionType": "ActionType_Pass" } }
                ]
              }
            }
            """));

        Assert.DoesNotContain(views, v => v.Decision.Kind == DecisionKind.MainPhase);

        engine.Apply(Evt(2, """
            {
              "type": "GREMessageType_GameStateMessage",
              "systemSeatIds": [1],
              "gameStateMessage": {
                "type": "GameStateType_Diff",
                "turnInfo": {
                  "phase": "Phase_Main1",
                  "step": "Step_Begin",
                  "turnNumber": 1,
                  "activePlayer": 1,
                  "priorityPlayer": 1,
                  "decisionPlayer": 1
                },
                "actions": [
                  { "seatId": 1, "action": { "actionType": "ActionType_Cast", "instanceId": 159 } },
                  { "seatId": 1, "action": { "actionType": "ActionType_Play", "instanceId": 160 } },
                  { "seatId": 1, "action": { "actionType": "ActionType_Pass" } }
                ]
              }
            }
            """));

        Assert.Contains(views, v => v.Decision.Kind == DecisionKind.MainPhase);
        Assert.Equal(DecisionKind.MainPhase, views.Last().Decision.Kind);
    }

    private static GreEvent Evt(ulong seq, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.Clone();
        var type = root.GetProperty("type").GetString() ?? string.Empty;
        return new GreEvent(seq, null, "line", new GreMessage(type, root, GreTrafficDirection.GreToClient), 0);
    }
}
