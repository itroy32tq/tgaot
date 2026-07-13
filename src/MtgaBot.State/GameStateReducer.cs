using System.Text.Json;
using MtgaBot.State.Internal;

namespace MtgaBot.State;

public sealed class GameStateReducer
{
    private static readonly string[] GameStateKeys =
    [
        "turnInfo",
        "timers",
        "gameObjects",
        "players",
        "annotations",
        "actions",
        "zones",
        "pendingMessageCount",
    ];

    internal MutableGameState State { get; } = new();

    public void Reset() => State.Reset();

    public void ApplyGameStateMessage(JsonElement gameStateMessage)
    {
        if (gameStateMessage.TryGetProperty("type", out var typeElement)
            && typeElement.GetString() == "GameStateType_Full")
        {
            State.Reset();
            ApplyDiff(gameStateMessage);
            return;
        }

        ApplyDiff(gameStateMessage);
    }

    public void ApplyTimerStateMessage(JsonElement timerStateMessage)
    {
        if (!timerStateMessage.TryGetProperty("timers", out var timersElement)
            || timersElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var updates = JsonMergeHelper.ParseObjectArray(timersElement);
        State.Timers.Clear();
        State.Timers.AddRange(JsonMergeHelper.MergeListByKey([], updates, "timerId"));
    }

    private void ApplyDiff(JsonElement diff)
    {
        var previousZones = State.Zones.ToList();
        var previousObjects = State.GameObjects.ToList();
        var previousPlayers = State.Players.ToList();
        var previousTimers = State.Timers.ToList();
        var previousAnnotations = State.Annotations.ToList();

        if (diff.TryGetProperty("turnInfo", out var turnInfoElement))
        {
            State.Turn = ParseTurnInfo(turnInfoElement);
        }

        if (diff.TryGetProperty("pendingMessageCount", out var pendingElement)
            && pendingElement.TryGetInt32(out var pendingCount))
        {
            State.PendingMessageCount = pendingCount;
        }

        var deletedInstanceIds = diff.TryGetProperty("diffDeletedInstanceIds", out var deletedInstances)
            ? JsonMergeHelper.ParseIntArray(deletedInstances)
            : null;
        var deletedAnnotationIds = diff.TryGetProperty("diffDeletedAnnotationIds", out var deletedAnnotations)
            ? JsonMergeHelper.ParseIntArray(deletedAnnotations)
            : null;

        if (diff.TryGetProperty("zones", out var zonesElement))
        {
            var updates = JsonMergeHelper.ParseObjectArray(zonesElement);
            State.Zones.Clear();
            State.Zones.AddRange(JsonMergeHelper.MergeListByKey(previousZones, updates, "zoneId"));
        }

        if (diff.TryGetProperty("gameObjects", out _) || deletedInstanceIds is { Count: > 0 })
        {
            var updates = diff.TryGetProperty("gameObjects", out var objectsElement)
                && objectsElement.ValueKind == JsonValueKind.Array
                ? JsonMergeHelper.ParseObjectArray(objectsElement)
                : [];
            State.GameObjects.Clear();
            State.GameObjects.AddRange(
                JsonMergeHelper.MergeListByKey(previousObjects, updates, "instanceId", deletedInstanceIds));
        }

        if (diff.TryGetProperty("players", out var playersElement))
        {
            var updates = JsonMergeHelper.ParseObjectArray(playersElement);
            State.Players.Clear();
            State.Players.AddRange(JsonMergeHelper.MergeListByKey(previousPlayers, updates, "systemSeatNumber"));
        }

        if (diff.TryGetProperty("timers", out var timersElement))
        {
            var updates = JsonMergeHelper.ParseObjectArray(timersElement);
            State.Timers.Clear();
            State.Timers.AddRange(JsonMergeHelper.MergeListByKey(previousTimers, updates, "timerId"));
        }

        if (diff.TryGetProperty("annotations", out _) || deletedAnnotationIds is { Count: > 0 })
        {
            var updates = diff.TryGetProperty("annotations", out var annotationsElement)
                && annotationsElement.ValueKind == JsonValueKind.Array
                ? JsonMergeHelper.ParseObjectArray(annotationsElement)
                : [];
            State.Annotations.Clear();
            State.Annotations.AddRange(
                JsonMergeHelper.MergeListByKey(previousAnnotations, updates, "id", deletedAnnotationIds));
        }

        if (diff.TryGetProperty("actions", out var actionsElement))
        {
            State.Actions.Clear();
            State.Actions.AddRange(ParseLegalActions(actionsElement));
        }
    }

    internal static TurnInfo ParseTurnInfo(JsonElement turnInfo)
    {
        static int ReadInt(JsonElement element, string property, int fallback = 0) =>
            element.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed)
                ? parsed
                : fallback;

        static string ReadString(JsonElement element, string property, string fallback = "") =>
            element.TryGetProperty(property, out var value) ? value.GetString() ?? fallback : fallback;

        return new TurnInfo(
            ReadString(turnInfo, "phase", "Phase_Unknown"),
            ReadString(turnInfo, "step", "Step_Unknown"),
            ReadInt(turnInfo, "turnNumber"),
            ReadInt(turnInfo, "activePlayer"),
            ReadInt(turnInfo, "priorityPlayer"),
            ReadInt(turnInfo, "decisionPlayer"));
    }

    internal static IReadOnlyList<LegalAction> ParseLegalActions(JsonElement actionsElement)
    {
        var actions = new List<LegalAction>();
        foreach (var entry in actionsElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("action", out var actionElement))
            {
                continue;
            }

            var seatId = entry.TryGetProperty("seatId", out var seatElement) && seatElement.TryGetInt32(out var seat)
                ? seat
                : 0;
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
}
