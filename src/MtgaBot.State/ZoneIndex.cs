using System.Text.Json;
using MtgaBot.State.Internal;

namespace MtgaBot.State;

internal sealed class ZoneIndex
{
    public IReadOnlyList<int> GetInstanceIds(MutableGameState state, string zoneType, int ownerSeatId)
    {
        var matches = state.Zones
            .Where(zone => zone.TryGetValue("type", out var type) && type.GetString() == zoneType)
            .ToList();

        Dictionary<string, JsonElement>? selected = null;
        if (matches.Count == 1)
        {
            selected = matches[0];
        }
        else
        {
            selected = matches.FirstOrDefault(zone =>
                zone.TryGetValue("ownerSeatId", out var owner)
                && owner.TryGetInt32(out var seat)
                && seat == ownerSeatId);
        }

        if (selected is null
            || !selected.TryGetValue("objectInstanceIds", out var idsElement)
            || idsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<int>();
        }

        var ids = new List<int>();
        foreach (var idElement in idsElement.EnumerateArray())
        {
            if (idElement.TryGetInt32(out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
