using System.Text.Json;
using MtgaBot.State.Internal;

namespace MtgaBot.State;

internal sealed class AnnotationTracker
{
    public int RemoveByType(MutableGameState state, string annotationType, int? affectorId = null)
    {
        var removed = 0;
        var kept = new List<Dictionary<string, JsonElement>>();

        foreach (var annotation in state.Annotations)
        {
            if (!annotation.TryGetValue("type", out var typesElement) || typesElement.ValueKind != JsonValueKind.Array)
            {
                kept.Add(annotation);
                continue;
            }

            var types = typesElement.EnumerateArray()
                .Select(item => item.GetString())
                .Where(type => type is not null)
                .Cast<string>()
                .ToList();

            var matchesAffector = affectorId is null
                || !annotation.TryGetValue("affectorId", out var affectorElement)
                || !affectorElement.TryGetInt32(out var parsedAffector)
                || parsedAffector == affectorId.Value;

            if (types.Contains(annotationType) && matchesAffector)
            {
                removed++;
                continue;
            }

            kept.Add(annotation);
        }

        if (removed > 0)
        {
            state.Annotations.Clear();
            state.Annotations.AddRange(kept);
        }

        return removed;
    }
}
