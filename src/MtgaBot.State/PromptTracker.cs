using System.Text.Json;

namespace MtgaBot.State;

internal sealed class PromptTracker
{
    private DecisionKind _kind = DecisionKind.None;
    private int _systemSeatId;
    private IReadOnlyList<LegalAction> _legalActions = Array.Empty<LegalAction>();
    private PromptContext? _prompt;

    public void Clear()
    {
        _kind = DecisionKind.None;
        _systemSeatId = 0;
        _legalActions = Array.Empty<LegalAction>();
        _prompt = null;
    }

    public void ApplyGreRequest(string messageType, JsonElement message)
    {
        _kind = MapKind(messageType);
        _systemSeatId = ReadSingleSeatId(message);
        _legalActions =
        [
            new LegalAction("Prompt", null, _systemSeatId, null),
        ];
        _prompt = BuildPrompt(messageType, message);
    }

    public void ApplyActionsAvailable(JsonElement message)
    {
        _kind = DecisionKind.MainPhase;
        _systemSeatId = ReadSingleSeatId(message);
        if (message.TryGetProperty("actionsAvailableReq", out var reqElement)
            && reqElement.TryGetProperty("actions", out var actionsElement))
        {
            _legalActions = ParseFlatActions(actionsElement, _systemSeatId);
        }
        else
        {
            _legalActions = Array.Empty<LegalAction>();
        }

        _prompt = null;
    }

    public void ApplyGameStateActions(IReadOnlyList<LegalAction> actions, int seatId, TurnInfo turn)
    {
        if (actions.Count == 0)
        {
            return;
        }

        _legalActions = actions;
        _systemSeatId = seatId;
        _kind = InferMainPhaseKind(turn);
        _prompt = null;
    }

    public DecisionPoint? BuildDecisionPoint(ulong decisionId)
    {
        if (_kind == DecisionKind.None)
        {
            return null;
        }

        return new DecisionPoint(decisionId, _kind, _systemSeatId, _legalActions, _prompt);
    }

    private static IReadOnlyList<LegalAction> ParseFlatActions(JsonElement actionsElement, int seatId)
    {
        var actions = new List<LegalAction>();
        foreach (var actionElement in actionsElement.EnumerateArray())
        {
            var actionType = actionElement.TryGetProperty("actionType", out var typeElement)
                ? typeElement.GetString() ?? string.Empty
                : string.Empty;
            int? instanceId = actionElement.TryGetProperty("instanceId", out var instanceElement)
                && instanceElement.TryGetInt32(out var parsedInstance)
                ? parsedInstance
                : null;

            actions.Add(new LegalAction(actionType, instanceId, seatId, null));
        }

        return actions;
    }

    private static DecisionKind MapKind(string messageType) => messageType switch
    {
        "GREMessageType_MulliganReq" => DecisionKind.Mulligan,
        "GREMessageType_SelectTargetsReq" => DecisionKind.SelectTargets,
        "GREMessageType_DeclareAttackersReq" => DecisionKind.Attackers,
        "GREMessageType_DeclareBlockersReq" => DecisionKind.Blockers,
        "GREMessageType_SelectNReq" => DecisionKind.SelectN,
        "GREMessageType_GroupReq" => DecisionKind.GroupReq,
        "GREMessageType_PayCostsReq" => DecisionKind.PayCosts,
        "GREMessageType_CastingTimeOptionsReq" => DecisionKind.CastingTimeOptions,
        "GREMessageType_AssignDamageReq" => DecisionKind.AssignDamage,
        _ => DecisionKind.None,
    };

    private static DecisionKind InferMainPhaseKind(TurnInfo turn)
    {
        if (turn.Phase == "Phase_Combat" && turn.Step == "Step_DeclareAttack")
        {
            return DecisionKind.Attackers;
        }

        return DecisionKind.MainPhase;
    }

    private static int ReadSingleSeatId(JsonElement message)
    {
        if (!message.TryGetProperty("systemSeatIds", out var seatsElement)
            || seatsElement.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        foreach (var seat in seatsElement.EnumerateArray())
        {
            if (seat.TryGetInt32(out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static PromptContext? BuildPrompt(string messageType, JsonElement message)
    {
        return messageType switch
        {
            "GREMessageType_SelectTargetsReq" => BuildSelectTargetsPrompt(message),
            "GREMessageType_SelectNReq" => BuildSelectNPrompt(message),
            _ => null,
        };
    }

    private static PromptContext? BuildSelectTargetsPrompt(JsonElement message)
    {
        if (!message.TryGetProperty("selectTargetsReq", out var req))
        {
            return null;
        }

        int? min = req.TryGetProperty("minTargets", out var minElement) && minElement.TryGetInt32(out var minValue)
            ? minValue
            : null;
        int? max = req.TryGetProperty("maxTargets", out var maxElement) && maxElement.TryGetInt32(out var maxValue)
            ? maxValue
            : null;

        return new PromptContext(min, max, null);
    }

    private static PromptContext? BuildSelectNPrompt(JsonElement message)
    {
        if (!message.TryGetProperty("selectNReq", out var req))
        {
            return null;
        }

        int? min = req.TryGetProperty("minSelections", out var minElement) && minElement.TryGetInt32(out var minValue)
            ? minValue
            : null;
        int? max = req.TryGetProperty("maxSelections", out var maxElement) && maxElement.TryGetInt32(out var maxValue)
            ? maxValue
            : null;

        return new PromptContext(min, max, null);
    }
}
