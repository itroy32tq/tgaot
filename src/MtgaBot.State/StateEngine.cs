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

        if (type == "GREMessageType_GameStateMessage"
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
        var decision = _promptTracker.BuildDecisionPoint(_currentDecisionId);
        if (decision is null || !_decisionGate.CanDecide(snapshot, decision, ActuatorBusy))
        {
            return null;
        }

        return new GameView(snapshot, decision, _lifecycle.Phase);
    }

    public GameSnapshot? TryGetSnapshot()
    {
        if (_mySeatId is not int seatId)
        {
            return null;
        }

        return _snapshotBuilder.Build(_reducer.State, seatId);
    }

    private void ApplyGameStateMessage(JsonElement message)
    {
        if (!message.TryGetProperty("gameStateMessage", out var gameStateMessage))
        {
            return;
        }

        _reducer.ApplyGameStateMessage(gameStateMessage);

        if (_reducer.State.Actions.Count > 0 && _mySeatId is int seatId)
        {
            var seatActions = _reducer.State.Actions.Where(action => action.SeatId == seatId).ToList();
            _promptTracker.ApplyGameStateActions(seatActions, seatId, _reducer.State.Turn);
        }
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

        foreach (var seat in seatsElement.EnumerateArray())
        {
            if (seat.TryGetInt32(out var parsed))
            {
                return parsed;
            }
        }

        _ = preferPromptTypes;
        return 0;
    }

    private void TryEmitDecision()
    {
        var view = TryGetDecisionView();
        if (view is null)
        {
            return;
        }

        if (view.Decision.DecisionId == _lastEmittedDecisionId)
        {
            return;
        }

        _lastEmittedDecisionId = view.Decision.DecisionId;
        DecisionReady?.Invoke(view);
    }
}
