using System.Text.Json;
using MtgaBot.State;
using MtgaBot.State.Internal;

namespace MtgaBot.State.Tests;

public class AnnotationTrackerTests
{
    [Fact]
    public void RemoveByType_PurgesTransientSelectingTargetsAnnotation()
    {
        var state = new MutableGameState();
        state.Annotations.Add(new Dictionary<string, JsonElement>
        {
            ["id"] = JsonDocument.Parse("7").RootElement,
            ["type"] = JsonDocument.Parse("[\"AnnotationType_PlayerSelectingTargets\"]").RootElement,
            ["affectorId"] = JsonDocument.Parse("1").RootElement,
        });

        var tracker = new AnnotationTracker();
        var removed = tracker.RemoveByType(state, "AnnotationType_PlayerSelectingTargets", affectorId: 1);

        Assert.Equal(1, removed);
        Assert.Empty(state.Annotations);
    }
}
