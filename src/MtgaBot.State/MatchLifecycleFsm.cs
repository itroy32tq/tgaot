using System.Text.Json;

namespace MtgaBot.State;

internal sealed class MatchLifecycleFsm
{
    public MatchPhase Phase { get; private set; } = MatchPhase.OutOfGame;

    public void ApplyGreMessage(string messageType, JsonElement message)
    {
        if (messageType is "GREMessageType_GameStateMessage" or "GREMessageType_QueuedGameStateMessage")
        {
            if (message.TryGetProperty("gameStateMessage", out var gameState)
                && gameState.TryGetProperty("type", out var typeElement)
                && typeElement.GetString() is "GameStateType_Full" or "GameStateType_Diff")
            {
                Phase = MatchPhase.InMatch;
            }

            return;
        }

        if (messageType == "GREMessageType_MulliganReq")
        {
            Phase = MatchPhase.InMatch;
        }
    }

    public void ApplyMatchState(JsonElement matchState)
    {
        if (!matchState.TryGetProperty("state", out var stateElement))
        {
            return;
        }

        var state = stateElement.GetString();
        if (state == "MatchGameRoomStateType_MatchCompleted")
        {
            Phase = MatchPhase.PostMatch;
        }
        else if (state is "MatchGameRoomStateType_Playing" or "MatchGameRoomStateType_MatchInProgress")
        {
            Phase = MatchPhase.InMatch;
        }
    }

    public void Reset() => Phase = MatchPhase.OutOfGame;
}
