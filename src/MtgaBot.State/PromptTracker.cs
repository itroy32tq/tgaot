using System.Text.Json;

namespace MtgaBot.State;

internal sealed class PromptTracker
{
    private static readonly HashSet<DecisionKind> LockedPromptKinds =
    [
        DecisionKind.Mulligan,
        DecisionKind.SelectTargets,
        DecisionKind.Attackers,
        DecisionKind.Blockers,
        DecisionKind.SelectN,
        DecisionKind.GroupReq,
        DecisionKind.PayCosts,
        DecisionKind.CastingTimeOptions,
        DecisionKind.AssignDamage,
    ];

    private DecisionKind _kind = DecisionKind.None;
    private int _systemSeatId;
    private IReadOnlyList<LegalAction> _legalActions = Array.Empty<LegalAction>();
    private PromptContext? _prompt;
    private bool _promptLocked;

    public void Clear()
    {
        _kind = DecisionKind.None;
        _systemSeatId = 0;
        _legalActions = Array.Empty<LegalAction>();
        _prompt = null;
        _promptLocked = false;
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
        _promptLocked = LockedPromptKinds.Contains(_kind);
    }

    public void ApplyActionsAvailable(JsonElement message)
    {
        _promptLocked = false;
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
        // Do not let sticky GameState Cast/Play lists overwrite DeclareAttackers/etc.
        if (_promptLocked)
        {
            if (ShouldUnlockLockedPrompt(turn))
            {
                _promptLocked = false;
            }
            else
            {
                return;
            }
        }

        // Opening-hand GameState arrives before MulliganReq with turn=0 / Phase_Unknown
        // and a fake Cast/Play list — that is not a real MainPhase decision.
        if (!IsPlayablePriorityWindow(turn))
        {
            return;
        }

        if (actions.Count == 0)
        {
            Clear();
            return;
        }

        _systemSeatId = seatId;
        _kind = InferMainPhaseKind(turn);
        _prompt = null;

        // Combat declare windows inferred from turnInfo should not surface sticky
        // Cast/Play lists from an earlier MainPhase Diff.
        if (_kind is DecisionKind.Attackers or DecisionKind.Blockers)
        {
            _legalActions = [new LegalAction("Prompt", null, seatId, null)];
            _promptLocked = true;
            return;
        }

        _legalActions = actions;
    }

    public DecisionPoint? BuildDecisionPoint(ulong decisionId)
    {
        if (_kind == DecisionKind.None)
        {
            return null;
        }

        return new DecisionPoint(decisionId, _kind, _systemSeatId, _legalActions, _prompt);
    }

    public DecisionPoint? BuildDecisionPoint(ulong decisionId, GameSnapshot board)
    {
        var decision = BuildDecisionPoint(decisionId);
        if (decision is null)
        {
            return null;
        }

        if (_promptLocked)
        {
            return decision;
        }

        var pruned = LegalActionFilter.PruneAgainstBoard(decision.LegalActions, board);
        if (pruned.Count == 0)
        {
            return null;
        }

        return pruned.SequenceEqual(decision.LegalActions)
            ? decision
            : decision with { LegalActions = pruned };
    }

    private bool ShouldUnlockLockedPrompt(TurnInfo turn)
    {
        return _kind switch
        {
            DecisionKind.Attackers => turn.Step != "Step_DeclareAttack",
            DecisionKind.Blockers => turn.Step != "Step_DeclareBlock",
            // After London keep/mulligan response, real turns start — release the lock.
            DecisionKind.Mulligan => IsPlayablePriorityWindow(turn),
            _ => turn.Phase is "Phase_Main1" or "Phase_Main2",
        };
    }

    private static bool IsPlayablePriorityWindow(TurnInfo turn) =>
        turn.TurnNumber > 0
        && !string.IsNullOrEmpty(turn.Phase)
        && turn.Phase is not "Phase_Unknown";

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

        if (turn.Phase == "Phase_Combat" && turn.Step == "Step_DeclareBlock")
        {
            return DecisionKind.Blockers;
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

        // PromptReq often broadcasts [1,2] — ignore ambiguous multi-seat lists.
        var seats = new List<int>();
        foreach (var seat in seatsElement.EnumerateArray())
        {
            if (seat.TryGetInt32(out var parsed) && parsed > 0)
            {
                seats.Add(parsed);
            }
        }

        return seats.Count == 1 ? seats[0] : 0;
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
