using MtgaBot.State;

namespace MtgaBot.Decide;

/// <summary>
/// GRE-side confirmation that a hand card actually left the hand zone.
/// </summary>
public static class HandActionAck
{
    /// <summary>
    /// Land/cast is confirmed only when <paramref name="instanceId"/> is no longer in hand.
    /// "Play disappeared from legal" alone is a false positive (sticky Diff / empty actions).
    /// </summary>
    public static bool IsConfirmed(GameSnapshot board, DecisionPoint? decision, int instanceId)
    {
        ArgumentNullException.ThrowIfNull(board);
        _ = decision;
        return !board.HandInstanceIds.Contains(instanceId);
    }

    /// <summary>
    /// Stricter check: card was in hand before the click and is gone after.
    /// </summary>
    public static bool IsLeftHand(GameSnapshot before, GameSnapshot after, int instanceId)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return before.HandInstanceIds.Contains(instanceId)
               && !after.HandInstanceIds.Contains(instanceId);
    }
}
