using System.Text.Json;
using MtgaBot.State.Internal;

namespace MtgaBot.State;

internal sealed class ObjectRegistry
{
    public IReadOnlyDictionary<int, CardView> Build(MutableGameState state, ZoneIndex zoneIndex)
    {
        var zoneById = state.Zones
            .Where(zone => zone.TryGetValue("zoneId", out _))
            .ToDictionary(
                zone => zone["zoneId"].GetInt32(),
                zone => zone.TryGetValue("type", out var type) ? type.GetString() ?? string.Empty : string.Empty);

        var objects = new Dictionary<int, CardView>();
        foreach (var gameObject in state.GameObjects)
        {
            if (!gameObject.TryGetValue("instanceId", out var instanceElement)
                || !instanceElement.TryGetInt32(out var instanceId))
            {
                continue;
            }

            var grpId = gameObject.TryGetValue("grpId", out var grpElement) && grpElement.TryGetInt32(out var parsedGrp)
                ? parsedGrp
                : 0;
            var zoneId = gameObject.TryGetValue("zoneId", out var zoneElement) && zoneElement.TryGetInt32(out var parsedZone)
                ? parsedZone
                : 0;
            var ownerSeatId = gameObject.TryGetValue("ownerSeatId", out var ownerElement)
                && ownerElement.TryGetInt32(out var parsedOwner)
                ? parsedOwner
                : 0;
            var tapped = gameObject.TryGetValue("isTapped", out var tappedElement)
                && tappedElement.ValueKind == JsonValueKind.True;
            var zoneType = zoneById.TryGetValue(zoneId, out var parsedZoneType) ? parsedZoneType : string.Empty;

            objects[instanceId] = new CardView(instanceId, grpId, zoneId, zoneType, ownerSeatId, tapped);
        }

        _ = zoneIndex;
        return objects;
    }
}
