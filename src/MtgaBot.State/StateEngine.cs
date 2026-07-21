using System.Text.Json;
using MtgaBot.Ingest;

namespace MtgaBot.State;

public sealed class StateEngine : IStateEngine
{
    private static readonly HashSet<string> SeatDetectionTypes =
    [
        "GREMessageType_ActionsAvailableReq",
        "GREMessageType_SelectNReq",
        "GREMessageType_SelectTargetsReq",
        "GREMessageType_DeclareAttackersReq",
        "GREMessageType_AssignDamageReq",
        "GREMessageType_MulliganReq",
    ];

    private static readonly HashSet<string> GreRequestTypes =
    [
        "GREMessageType_MulliganReq",
        "GREMessageType_SelectTargetsReq",
        "GREMessageType_DeclareAttackersReq",
        "GREMessageType_DeclareBlockersReq",
        "GREMessageType_SelectNReq",
        "GREMessageType_GroupReq",
        "GREMessageType_PayCostsReq",
        "GREMessageType_CastingTimeOptionsReq",
        "GREMessageType_AssignDamageReq",
    ];

    private readonly GameStateReducer _reducer = new();
    private readonly PromptTracker _promptTracker = new();
    private readonly AnnotationTracker _annotationTracker = new();
    private readonly MatchLifecycleFsm _lifecycle = new();
    private readonly DecisionGate _decisionGate = new();
    private readonly GameSnapshotBuilder _snapshotBuilder = new();

    private int? _mySeatId;
    private ulong _currentDecisionId;
    private ulong _lastEmittedDecisionId;
    private string? _lastEmittedSignature;

    public event Action<GameView>? DecisionReady;

    public bool ActuatorBusy { get; set; }

    public void Apply(GreEvent evt)
    {
        var message = evt.Message;
        var payload = message.Payload;
        var type = message.Type;

        UpdateSeatId(type, payload);

        switch (type)
        {
            case "GREMessageType_GameStateMessage":
            case "GREMessageType_QueuedGameStateMessage":
                ApplyGameStateMessage(payload);
                break;
            case "GREMessageType_TimerStateMessage":
                if (payload.TryGetProperty("timerStateMessage", out var timerState))
                {
                    _reducer.ApplyTimerStateMessage(timerState);
                }

                break;
            case "GREMessageType_ActionsAvailableReq":
                _promptTracker.ApplyActionsAvailable(payload);
                _currentDecisionId = evt.Sequence;
                break;
            case "ClientMessageType_SelectTargetsResp":
                _annotationTracker.RemoveByType(_reducer.State, "AnnotationType_PlayerSelectingTargets", _mySeatId);
                break;
            case "ClientMessageType_MulliganResp":
                // Keep/mulligan answered — unlock so the next MainPhase can open.
                _promptTracker.Clear();
                break;
            case "MatchGameRoomStateChanged":
                _lifecycle.ApplyMatchState(payload);
                break;
            default:
                if (GreRequestTypes.Contains(type))
                {
                    _promptTracker.ApplyGreRequest(type, payload);
                    _currentDecisionId = evt.Sequence;
                }

                break;
        }

        if ((type is "GREMessageType_GameStateMessage" or "GREMessageType_QueuedGameStateMessage")
            && _reducer.State.Actions.Count > 0
            && _mySeatId is not null)
        {
            _currentDecisionId = evt.Sequence;
        }

        _lifecycle.ApplyGreMessage(type, payload);
        TryEmitDecision();
    }

    public GameView? TryGetDecisionView()
    {
        if (_mySeatId is not int seatId)
        {
            return null;
        }

        var snapshot = _snapshotBuilder.Build(_reducer.State, seatId);
        var decision = _promptTracker.BuildDecisionPoint(_currentDecisionId, snapshot);
        if (decision is null || !_decisionGate.CanDecide(snapshot, decision, ActuatorBusy))
        {
            return null;
        }

        return new GameView(snapshot, decision, _lifecycle.Phase);
    }

    /// <summary>
    /// Decision point without ActuatorBusy gate — for GRE ack while clicking.
    /// </summary>
    public DecisionPoint? TryGetDecisionPointRaw()
    {
        if (_mySeatId is not int seatId)
        {
            return null;
        }

        var snapshot = _snapshotBuilder.Build(_reducer.State, seatId);
        return _promptTracker.BuildDecisionPoint(_currentDecisionId, snapshot);
    }

    public GameSnapshot? TryGetSnapshot()
    {
        if (_mySeatId is not int seatId)
        {
            return null;
        }

        return _snapshotBuilder.Build(_reducer.State, seatId);
    }

    /// <summary>
    /// UI Keep was clicked — do not Clear Mulligan lock (needed for Main1 sticky unlock).
    /// </summary>
    public void AcknowledgeMulliganAnswered()
    {
        _promptTracker.MarkMulliganUiAnswered();
        _lastEmittedDecisionId = 0;
        _lastEmittedSignature = null;
        TryPromoteStickyPriority();
    }

    /// <summary>
    /// When prompt is empty but reducer still has Play/Cast and turn is Main1/Main2, open a decision.
    /// </summary>
    public bool TryPromoteStickyPriority()
    {
        if (_mySeatId is not int seatId)
        {
            return false;
        }

        var seatActions = _reducer.State.Actions.Where(action => action.SeatId == seatId).ToList();
        if (!_promptTracker.TryOpenPlayableFromSticky(_reducer.State.Turn, seatActions, seatId))
        {
            return false;
        }

        _currentDecisionId++;
        _lastEmittedSignature = null;
        TryEmitDecision();
        return true;
    }

    /// <summary>
    /// After UI actuate, GRE may have changed the board while ActuatorBusy blocked emit.
    /// Call with busy cleared so Pass/Cast can continue without waiting for a new log line.
    /// </summary>
    public void TryEmitAfterActuate()
    {
        TryPromoteStickyPriority();
        TryEmitDecision();
    }

    private void ApplyGameStateMessage(JsonElement message)
    {
        if (!message.TryGetProperty("gameStateMessage", out var gameStateMessage))
        {
            return;
        }

        var isFull = gameStateMessage.TryGetProperty("type", out var typeElement)
            && typeElement.GetString() == "GameStateType_Full";
        var actionsPresent = gameStateMessage.TryGetProperty("actions", out _);

        _reducer.ApplyGameStateMessage(gameStateMessage);

        if (isFull)
        {
            _promptTracker.Clear();
        }

        if (_mySeatId is not int seatId)
        {
            return;
        }

        var seatActions = _reducer.State.Actions.Where(action => action.SeatId == seatId).ToList();

        // Omitting actions means "unchanged" — still open/unlock playable windows from sticky lists.
        if (!actionsPresent && !isFull)
        {
            if (_promptTracker.TryUnlockMulliganForTurn(_reducer.State.Turn, seatActions, seatId)
                || _promptTracker.TryOpenPlayableFromSticky(_reducer.State.Turn, seatActions, seatId))
            {
                _currentDecisionId++;
                _lastEmittedSignature = null;
            }

            return;
        }

        _promptTracker.ApplyGameStateActions(seatActions, seatId, _reducer.State.Turn);
    }

    private void UpdateSeatId(string messageType, JsonElement message)
    {
        if (!SeatDetectionTypes.Contains(messageType)
            && !message.TryGetProperty("systemSeatIds", out _))
        {
            return;
        }

        var seatId = ReadSingleSeatId(message, preferPromptTypes: SeatDetectionTypes.Contains(messageType));
        if (seatId > 0)
        {
            _mySeatId = seatId;
        }
    }

    private static int ReadSingleSeatId(JsonElement message, bool preferPromptTypes)
    {
        if (!message.TryGetProperty("systemSeatIds", out var seatsElement)
            || seatsElement.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var seats = new List<int>();
        foreach (var seat in seatsElement.EnumerateArray())
        {
            if (seat.TryGetInt32(out var parsed) && parsed > 0)
            {
                seats.Add(parsed);
            }
        }

        _ = preferPromptTypes;
        // Ambiguous multi-seat arrays (e.g. PromptReq [1,2]) must not overwrite MySeatId.
        return seats.Count == 1 ? seats[0] : 0;
    }

    private void TryEmitDecision()
    {
        var view = TryGetDecisionView();
        if (view is null)
        {
            return;
        }

        var signature = LegalActionFilter.BuildSignature(view);
        if (view.Decision.DecisionId == _lastEmittedDecisionId
            || string.Equals(signature, _lastEmittedSignature, StringComparison.Ordinal))
        {
            return;
        }

        _lastEmittedDecisionId = view.Decision.DecisionId;
        _lastEmittedSignature = signature;
        DecisionReady?.Invoke(view);
    }
}
