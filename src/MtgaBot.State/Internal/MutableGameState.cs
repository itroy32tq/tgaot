using System.Text.Json;

namespace MtgaBot.State.Internal;
internal sealed class MutableGameState
{
    public TurnInfo Turn { get; set; } = new("Phase_Unknown", "Step_Unknown", 0, 0, 0, 0);

    public List<Dictionary<string, JsonElement>> Zones { get; } = [];

    public List<Dictionary<string, JsonElement>> GameObjects { get; } = [];

    public List<Dictionary<string, JsonElement>> Players { get; } = [];

    public List<Dictionary<string, JsonElement>> Annotations { get; } = [];

    public List<Dictionary<string, JsonElement>> Timers { get; } = [];

    public List<LegalAction> Actions { get; } = [];

    public int PendingMessageCount { get; set; }

    public void Reset()
    {
        Turn = new TurnInfo("Phase_Unknown", "Step_Unknown", 0, 0, 0, 0);
        Zones.Clear();
        GameObjects.Clear();
        Players.Clear();
        Annotations.Clear();
        Timers.Clear();
        Actions.Clear();
        PendingMessageCount = 0;
    }
}
