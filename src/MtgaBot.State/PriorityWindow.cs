namespace MtgaBot.State;

/// <summary>
/// Deterministic windows where hand actuate (land/cast) is allowed.
/// </summary>
public static class PriorityWindow
{
    public static bool IsMain1(TurnInfo turn) =>
        turn.TurnNumber > 0 && turn.Phase == "Phase_Main1";

    public static bool IsOurPriority(GameSnapshot board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var seat = board.MySeatId;
        var turn = board.Turn;
        if (turn.PriorityPlayer > 0)
        {
            return turn.PriorityPlayer == seat;
        }

        if (turn.DecisionPlayer > 0)
        {
            return turn.DecisionPlayer == seat;
        }

        return turn.ActivePlayer == seat;
    }

    /// <summary>Our turn, Main1 (priority may still be with the opponent).</summary>
    public static bool IsOurTurnMain1(GameSnapshot board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (!IsMain1(board.Turn))
        {
            return false;
        }

        var seat = board.MySeatId;
        var active = board.Turn.ActivePlayer;
        return active <= 0 || active == seat;
    }

    /// <summary>
    /// Land play: <em>our</em> Main1 while we are the active player and hold priority.
    /// Opponent Main1 with our priority (e.g. after they play a land) is not a land window.
    /// </summary>
    public static bool IsOurMain1(GameSnapshot board) =>
        IsOurTurnMain1(board) && IsOurPriority(board);
}
