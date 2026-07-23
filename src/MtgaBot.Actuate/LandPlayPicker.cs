namespace MtgaBot.Actuate;

/// <summary>
/// Chooses which inventoried land to play. Stub: leftmost playable land.
/// Later: color / mana accounting without moving the mouse.
/// </summary>
public static class LandPlayPicker
{
    public static HandCardHit? PickFirst(
        IReadOnlyList<HandCardHit> inventory,
        IReadOnlyCollection<int> playableLandIds)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(playableLandIds);

        if (inventory.Count == 0 || playableLandIds.Count == 0)
        {
            return null;
        }

        var set = playableLandIds as ISet<int> ?? playableLandIds.ToHashSet();
        HandCardHit? best = null;
        foreach (var hit in inventory)
        {
            if (!set.Contains(hit.InstanceId))
            {
                continue;
            }

            if (best is null || hit.DesignX < best.DesignX)
            {
                best = hit;
            }
        }

        return best;
    }
}
