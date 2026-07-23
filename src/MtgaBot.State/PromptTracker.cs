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
    private bool _mulliganUiAnswered;

    public void Clear()
    {
        _kind = DecisionKind.None;
        _systemSeatId = 0;
        _legalActions = Array.Empty<LegalAction>();
        _prompt = null;
        _promptLocked = false;
        _mulliganUiAnswered = false;
    }

    /// <summary>
    /// Keep was clicked — suppress further Mulligan decisions but keep the lock
    /// so Main1 Diff without actions can still unlock via sticky Play/Cast.
    /// </summary>
    public void MarkMulliganUiAnswered() => _mulliganUiAnswered = true;

    /// <summary>Allow Keep to be emitted again when the previous click was not confirmed by GRE.</summary>
    public void ClearMulliganUiAnswered() => _mulliganUiAnswered = false;

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

    /// <summary>
    /// After Keep, GRE may advance to Main1/Main2 without re-sending the actions array.
    /// Unlock Mulligan and surface sticky seat actions so live loop is not stuck.
    /// </summary>
    public bool TryUnlockMulliganForTurn(TurnInfo turn, IReadOnlyList<LegalAction> seatActions, int seatId)
    {
        if (_kind != DecisionKind.Mulligan || !_promptLocked)
        {
            return false;
        }

        if (!ShouldUnlockLockedPrompt(turn))
        {
            return false;
        }

        _promptLocked = false;
        _mulliganUiAnswered = false;
        if (seatActions.Count == 0 || !IsPlayablePriorityWindow(turn))
        {
            Clear();
            return true;
        }

        ApplyGameStateActions(seatActions, seatId, turn);
        return _kind != DecisionKind.None;
    }

    /// <summary>
    /// After Keep clears Mulligan, Main1 Diff often omits <c>actions</c>.
    /// Open MainPhase/Combat from sticky reducer actions while prompt is empty.
    /// </summary>
    public bool TryOpenPlayableFromSticky(TurnInfo turn, IReadOnlyList<LegalAction> seatActions, int seatId)
    {
        if (_promptLocked)
        {
            return TryUnlockMulliganForTurn(turn, seatActions, seatId);
        }

        if (_kind != DecisionKind.None)
        {
            return false;
        }

        if (seatActions.Count == 0)
        {
            return false;
        }

        if (!IsPlayablePriorityWindow(turn) && !IsCombatDeclareWindow(turn))
        {
            return false;
        }

        ApplyGameStateActions(seatActions, seatId, turn);
        return _kind != DecisionKind.None;
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
        // Combat declare windows still need to open (Attackers/Blockers).
        if (!IsPlayablePriorityWindow(turn) && !IsCombatDeclareWindow(turn))
        {
            return;
        }

        if (actions.Count == 0)
        {
            // Empty seat slice must not wipe an already-open MainPhase (parse/seat filter miss).
            if (_kind is DecisionKind.MainPhase or DecisionKind.Attackers or DecisionKind.Blockers)
            {
                return;
            }

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

        // Waiting for Main1 unlock after Keep — do not re-emit KeepHand.
        if (_kind == DecisionKind.Mulligan && _mulliganUiAnswered)
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

        // Sticky MainPhase from the previous turn often survives into our Beginning/Draw.
        // Emitting it makes LandOnly Pass through Next and skip Main1 (T1 works because
        // post-Keep is still Mulligan-locked; T3+ burns on draw).
        if (decision.Kind == DecisionKind.MainPhase
            && board.Turn.Phase == "Phase_Beginning"
            && board.Turn.TurnNumber > 0
            && board.MySeatId > 0
            && board.Turn.ActivePlayer == board.MySeatId)
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
            // Opening-hand sticky Play/Cast ids are often remapped after Keep.
            // Leaving MainPhase with only stale ids blocks forever (no DecisionReady).
            if (_kind == DecisionKind.MainPhase && !_promptLocked)
            {
                _kind = DecisionKind.None;
                _legalActions = Array.Empty<LegalAction>();
                _prompt = null;
            }

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
        && turn.Phase is "Phase_Main1" or "Phase_Main2";

    private static bool IsCombatDeclareWindow(TurnInfo turn) =>
        turn.TurnNumber > 0
        && turn.Phase == "Phase_Combat"
        && turn.Step is "Step_DeclareAttack" or "Step_DeclareBlock";

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
