using System.Text;
using MtgaBot.State;

namespace MtgaBot.Host.Shadow;

public interface IShadowReporter
{
    void OnStarted(ShadowOptions options);

    void OnDecision(GameView view);

    void OnReplayComplete(ShadowRunResult result);

    void OnFollowStarted();

    void OnError(string message);
}

public sealed class ShadowConsoleReporter : IShadowReporter
{
    private readonly TextWriter _output;

    public ShadowConsoleReporter(TextWriter? output = null)
    {
        _output = output ?? Console.Out;
    }

    public void OnStarted(ShadowOptions options)
    {
        _output.WriteLine(
            $"MtgaBot shadow ({MtgaBotHost.Version}) — Ingest+State, no clicks");
        _output.WriteLine(
            $"log: {options.LogPath}  mode: {(options.Follow ? "follow" : "replay")}");
        _output.WriteLine();
    }

    public void OnDecision(GameView view)
    {
        _output.WriteLine(FormatDecision(view));
    }

    public void OnReplayComplete(ShadowRunResult result)
    {
        _output.WriteLine();
        _output.WriteLine(
            $"done: events={result.EventCount} decisions={result.DecisionCount}");
    }

    public void OnFollowStarted()
    {
        _output.WriteLine("following… (Ctrl+C to stop)");
        _output.WriteLine();
    }

    public void OnError(string message)
    {
        _output.WriteLine($"error: {message}");
    }

    public static string FormatDecision(GameView view)
    {
        var board = view.Board;
        var turn = board.Turn;
        var phase = ShortPhase(turn.Phase);
        var legal = string.Join(", ", view.Decision.LegalActions.Select(FormatAction));
        if (string.IsNullOrEmpty(legal))
        {
            legal = "(none)";
        }

        var sb = new StringBuilder();
        sb.Append('[')
            .Append("turn ").Append(turn.TurnNumber).Append(' ').Append(phase)
            .Append("] decision=").Append(view.Decision.DecisionId)
            .Append(' ').Append(view.Decision.Kind)
            .Append(" | life ").Append(board.MyLife).Append('/').Append(board.OpponentLife)
            .Append(" hand=").Append(board.HandInstanceIds.Count)
            .Append(" bf=").Append(board.BattlefieldInstanceIds.Count)
            .Append(" stack=").Append(board.StackInstanceIds.Count)
            .Append(" | ").Append(view.Lifecycle);
        sb.AppendLine();
        sb.Append("  legal: ").Append(legal);
        return sb.ToString();
    }

    private static string FormatAction(LegalAction action)
    {
        var type = ShortActionType(action.ActionType);
        return action.InstanceId is int id ? $"{type}({id})" : type;
    }

    private static string ShortActionType(string actionType)
    {
        const string prefix = "ActionType_";
        return actionType.StartsWith(prefix, StringComparison.Ordinal)
            ? actionType[prefix.Length..]
            : actionType;
    }

    private static string ShortPhase(string phase)
    {
        const string prefix = "Phase_";
        if (phase.StartsWith(prefix, StringComparison.Ordinal))
        {
            return phase[prefix.Length..];
        }

        return string.IsNullOrEmpty(phase) ? "?" : phase;
    }
}
