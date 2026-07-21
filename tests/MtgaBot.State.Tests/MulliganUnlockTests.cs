using System.Text.Json;
using MtgaBot.Ingest;

namespace MtgaBot.State.Tests;

public class MulliganUnlockTests
{
    [Fact]
    public void Main1DiffWithoutActions_UnlocksMulligan_UsingStickyActions()
    {
        var engine = new StateEngine();
        var views = new List<GameView>();
        engine.DecisionReady += views.Add;

        engine.Apply(Evt(1,
            """
            {
              "type": "GREMessageType_MulliganReq",
              "systemSeatIds": [1],
              "mulliganReq": { "mulliganType": "MulliganType_London" }
            }
            """));
        Assert.Equal(DecisionKind.Mulligan, views.Last().Decision.Kind);

        // Seed sticky actions into reducer while Mulligan is locked (opening-hand Diff).
        engine.Apply(Evt(2,
            """
            {
              "type": "GREMessageType_GameStateMessage",
              "systemSeatIds": [1],
              "gameStateMessage": {
                "type": "GameStateType_Diff",
                "turnInfo": {
                  "turnNumber": 0,
                  "phase": "Phase_Unknown",
                  "step": "Step_Unknown",
                  "activePlayer": 1,
                  "decisionPlayer": 1
                },
                "zones": [
                  { "zoneId": 31, "type": "ZoneType_Hand", "ownerSeatId": 1, "objectInstanceIds": [10] }
                ],
                "gameObjects": [
                  { "instanceId": 10, "grpId": 1, "zoneId": 31, "ownerSeatId": 1 }
                ],
                "players": [
                  { "systemSeatNumber": 1, "lifeTotal": 20 },
                  { "systemSeatNumber": 2, "lifeTotal": 20 }
                ],
                "actions": [
                  { "seatId": 1, "action": { "actionType": "ActionType_Play", "instanceId": 10 } }
                ]
              }
            }
            """));
        Assert.Equal(DecisionKind.Mulligan, engine.TryGetDecisionView()!.Decision.Kind);

        views.Clear();

        // Main1 Diff omits actions (unchanged) — must unlock via sticky reducer actions.
        engine.Apply(Evt(3,
            """
            {
              "type": "GREMessageType_GameStateMessage",
              "systemSeatIds": [1],
              "gameStateMessage": {
                "type": "GameStateType_Diff",
                "turnInfo": {
                  "turnNumber": 1,
                  "phase": "Phase_Main1",
                  "step": "Step_Begin",
                  "activePlayer": 1,
                  "decisionPlayer": 1
                },
                "players": [
                  { "systemSeatNumber": 1, "lifeTotal": 20 },
                  { "systemSeatNumber": 2, "lifeTotal": 20 }
                ]
              }
            }
            """));

        Assert.Contains(views, v => v.Decision.Kind == DecisionKind.MainPhase);
        Assert.Equal(DecisionKind.MainPhase, engine.TryGetDecisionView()!.Decision.Kind);
        Assert.Contains(
            engine.TryGetDecisionView()!.Decision.LegalActions,
            a => a.ActionType.Contains("Play", StringComparison.Ordinal) && a.InstanceId == 10);
    }

    [Fact]
    public void AfterAcknowledge_Main1WithoutActions_OpensFromSticky()
    {
        var engine = new StateEngine();
        var views = new List<GameView>();
        engine.DecisionReady += views.Add;

        engine.Apply(Evt(1,
            """
            {
              "type": "GREMessageType_MulliganReq",
              "systemSeatIds": [1],
              "mulliganReq": { "mulliganType": "MulliganType_London" }
            }
            """));

        engine.Apply(Evt(2,
            """
            {
              "type": "GREMessageType_GameStateMessage",
              "systemSeatIds": [1],
              "gameStateMessage": {
                "type": "GameStateType_Diff",
                "turnInfo": {
                  "turnNumber": 0,
                  "phase": "Phase_Unknown",
                  "step": "Step_Unknown",
                  "activePlayer": 1,
                  "decisionPlayer": 1
                },
                "zones": [
                  { "zoneId": 31, "type": "ZoneType_Hand", "ownerSeatId": 1, "objectInstanceIds": [10] }
                ],
                "gameObjects": [
                  { "instanceId": 10, "grpId": 1, "zoneId": 31, "ownerSeatId": 1 }
                ],
                "players": [
                  { "systemSeatNumber": 1, "lifeTotal": 20 },
                  { "systemSeatNumber": 2, "lifeTotal": 20 }
                ],
                "actions": [
                  { "seatId": 1, "action": { "actionType": "ActionType_Play", "instanceId": 10 } }
                ]
              }
            }
            """));

        engine.AcknowledgeMulliganAnswered();
        Assert.Null(engine.TryGetDecisionView());
        views.Clear();

        engine.Apply(Evt(3,
            """
            {
              "type": "GREMessageType_GameStateMessage",
              "systemSeatIds": [1],
              "gameStateMessage": {
                "type": "GameStateType_Diff",
                "turnInfo": {
                  "turnNumber": 1,
                  "phase": "Phase_Main1",
                  "step": "Step_Begin",
                  "activePlayer": 1,
                  "decisionPlayer": 1
                },
                "players": [
                  { "systemSeatNumber": 1, "lifeTotal": 20 },
                  { "systemSeatNumber": 2, "lifeTotal": 20 }
                ]
              }
            }
            """));

        Assert.Contains(views, v => v.Decision.Kind == DecisionKind.MainPhase);
        Assert.NotNull(engine.TryGetDecisionView());
    }

    [Fact]
    public void AcknowledgeMulliganAnswered_ClearsDecision()
    {
        var engine = new StateEngine();
        engine.Apply(Evt(1,
            """
            {
              "type": "GREMessageType_MulliganReq",
              "systemSeatIds": [1],
              "mulliganReq": { "mulliganType": "MulliganType_London" }
            }
            """));

        Assert.NotNull(engine.TryGetDecisionView());
        engine.AcknowledgeMulliganAnswered();
        Assert.Null(engine.TryGetDecisionView());
    }

    private static GreEvent Evt(ulong seq, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.Clone();
        var msg = new GreMessage(root.GetProperty("type").GetString() ?? "", root, GreTrafficDirection.GreToClient);
        return new GreEvent(seq, null, "line", msg, 0);
    }
}
