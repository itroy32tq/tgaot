using System.Text;
using MtgaBot.Decide;
using MtgaBot.State;

namespace MtgaBot.Host.Shadow;

public interface IShadowReporter
{
    void OnStarted(ShadowOptions options, CardDatabaseResolver.ResolveResult cards);

    void OnDecision(GameView view, Intent intent);

    void OnReplayComplete(ShadowRunResult result);

    void OnFollowStarted();

    void OnGreEvent(int eventCount, string messageType);

    void OnError(string message);
}

public sealed class ShadowConsoleReporter(TextWriter? output = null) : IShadowReporter
{
    private readonly TextWriter _output = output ?? Console.Out;

    public void OnStarted(ShadowOptions options, CardDatabaseResolver.ResolveResult cards)
    {
        _output.WriteLine(
            $"MtgaBot shadow ({MtgaBotHost.Version}) — Ingest+State+Decide, no clicks");
        _output.WriteLine(
            $"log: {options.LogPath}  mode: {(options.Follow ? "follow" : "replay")}  policy: {options.PolicyName}");
        if (cards.CardsPath is null)
        {
            _output.WriteLine("cards: (none — FarmMvp will skip creature casts)");
        }
        else
        {
            _output.WriteLine($"cards: {cards.Count} from {cards.CardsPath}");
            if (cards.OverlayPath is not null)
            {
                _output.WriteLine($"cards overlay: {cards.OverlayPath}");
            }
        }

        _output.WriteLine();
    }

    public void OnDecision(GameView view, Intent intent)
    {
        _output.WriteLine(FormatDecision(view));
        _output.WriteLine($"  → Intent: {IntentFormatter.Format(intent)}");
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
        _output.WriteLine("waiting for new GRE lines after start (make in-game actions)");
        _output.WriteLine();
    }

    public void OnGreEvent(int eventCount, string messageType)
    {
        if (eventCount == 1)
        {
            _output.WriteLine($"ingest: receiving GRE events (first: {messageType})");
        }
        else if (eventCount % 50 == 0)
        {
            _output.WriteLine($"ingest: events={eventCount} (latest: {messageType})");
        }
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
        return action.InstanceId is { } id ? $"{type}({id})" : type;
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
