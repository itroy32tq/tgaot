namespace MtgaBot.State;

internal static class LegalActionFilter
{
    public static IReadOnlyList<LegalAction> PruneAgainstBoard(
        IReadOnlyList<LegalAction> actions,
        GameSnapshot board)
    {
        if (actions.Count == 0)
        {
            return actions;
        }

        var hand = board.HandInstanceIds as HashSet<int> ?? board.HandInstanceIds.ToHashSet();
        var battlefield = board.BattlefieldInstanceIds as HashSet<int>
            ?? board.BattlefieldInstanceIds.ToHashSet();

        var pruned = new List<LegalAction>(actions.Count);
        foreach (var action in actions)
        {
            if (action.InstanceId is not int instanceId)
            {
                pruned.Add(action);
                continue;
            }

            if (IsPlayOrCast(action.ActionType))
            {
                if (hand.Contains(instanceId))
                {
                    pruned.Add(action);
                }

                continue;
            }

            if (IsActivate(action.ActionType))
            {
                if (battlefield.Contains(instanceId) || hand.Contains(instanceId))
                {
                    pruned.Add(action);
                }

                continue;
            }

            pruned.Add(action);
        }

        return pruned;
    }

    public static string BuildSignature(GameView view)
    {
        var turn = view.Board.Turn;
        var legal = string.Join(
            ',',
            view.Decision.LegalActions
                .Select(a => $"{a.ActionType}:{a.InstanceId?.ToString() ?? "-"}")
                .Order(StringComparer.Ordinal));

        return string.Join(
            '|',
            turn.TurnNumber,
            turn.Phase,
            turn.Step,
            turn.ActivePlayer,
            turn.PriorityPlayer,
            view.Decision.Kind,
            view.Decision.SystemSeatId,
            view.Board.HandInstanceIds.Count,
            view.Board.BattlefieldInstanceIds.Count,
            legal);
    }

    private static bool IsPlayOrCast(string actionType) =>
        actionType.Contains("Play", StringComparison.OrdinalIgnoreCase)
        || actionType.Contains("Cast", StringComparison.OrdinalIgnoreCase);

    private static bool IsActivate(string actionType) =>
        actionType.Contains("Activate", StringComparison.OrdinalIgnoreCase);
}
