using System.Text.Json;
using MtgaBot.State.Internal;

namespace MtgaBot.State;

internal sealed class ZoneIndex
{
    /// <summary>
    /// Prefer membership from gameObjects.zoneId (authoritative for moved cards).
    /// Fall back to zones[].objectInstanceIds when objects are not yet loaded.
    /// </summary>
    public IReadOnlyList<int> GetInstanceIds(MutableGameState state, string zoneType, int ownerSeatId)
    {
        var fromObjects = GetInstanceIdsFromObjects(state, zoneType, ownerSeatId);
        if (fromObjects.Count > 0 || state.GameObjects.Count > 0)
        {
            return fromObjects;
        }

        return GetInstanceIdsFromZoneArray(state, zoneType, ownerSeatId);
    }

    private static IReadOnlyList<int> GetInstanceIdsFromObjects(
        MutableGameState state,
        string zoneType,
        int ownerSeatId)
    {
        var zoneIdToType = new Dictionary<int, string>();
        foreach (var zone in state.Zones)
        {
            if (!zone.TryGetValue("zoneId", out var zoneIdElement)
                || !zoneIdElement.TryGetInt32(out var zoneId)
                || !zone.TryGetValue("type", out var typeElement))
            {
                continue;
            }

            zoneIdToType[zoneId] = typeElement.GetString() ?? string.Empty;
        }

        var ids = new List<int>();
        foreach (var gameObject in state.GameObjects)
        {
            if (!gameObject.TryGetValue("instanceId", out var instanceElement)
                || !instanceElement.TryGetInt32(out var instanceId)
                || !gameObject.TryGetValue("zoneId", out var zoneElement)
                || !zoneElement.TryGetInt32(out var zoneId)
                || !zoneIdToType.TryGetValue(zoneId, out var type)
                || type != zoneType)
            {
                continue;
            }

            if (zoneType is "ZoneType_Battlefield" or "ZoneType_Stack" or "ZoneType_Exile"
                or "ZoneType_Graveyard" or "ZoneType_Limbo" or "ZoneType_Command")
            {
                // Public zones: filter by owner when present.
                if (gameObject.TryGetValue("ownerSeatId", out var ownerElement)
                    && ownerElement.TryGetInt32(out var owner)
                    && owner != ownerSeatId)
                {
                    continue;
                }

                ids.Add(instanceId);
                continue;
            }

            if (!gameObject.TryGetValue("ownerSeatId", out var handOwnerElement)
                || !handOwnerElement.TryGetInt32(out var handOwner)
                || handOwner != ownerSeatId)
            {
                continue;
            }

            ids.Add(instanceId);
        }

        return ids;
    }

    private static IReadOnlyList<int> GetInstanceIdsFromZoneArray(
        MutableGameState state,
        string zoneType,
        int ownerSeatId)
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
