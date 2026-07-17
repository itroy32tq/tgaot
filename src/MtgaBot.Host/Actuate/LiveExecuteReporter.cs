using MtgaBot.Actuate;
using MtgaBot.Decide;
using MtgaBot.State;

namespace MtgaBot.Host.Actuate;

public interface ILiveExecuteReporter
{
    void OnStarted(
        LiveExecuteOptions options,
        CardDatabaseResolver.ResolveResult cards,
        WindowRect? window,
        string? calibrationPath);
    void OnDecision(GameView view, Intent intent);
    void OnActuate(ActuateResult result);
    void OnError(string message);
    void OnInfo(string message);
}

public sealed class LiveExecuteConsoleReporter : ILiveExecuteReporter
{
    public void OnStarted(
        LiveExecuteOptions options,
        CardDatabaseResolver.ResolveResult cards,
        WindowRect? window,
        string? calibrationPath)
    {
        Console.WriteLine($"actuate live  dry-run={options.DryRun}  policy={options.PolicyName}");
        Console.WriteLine($"log: {options.LogPath}");
        Console.WriteLine(
            cards.Count > 0
                ? $"cards: {cards.Count} from {cards.CardsPath}"
                : "cards: empty (land-only / pass heuristics)");
        Console.WriteLine(
            calibrationPath is not null
                ? $"calibration: {calibrationPath}"
                : "calibration: in-code defaults (1920×1080)");
        if (window is { } rect)
        {
            Console.WriteLine($"window: {rect.Width}x{rect.Height} @ ({rect.Left},{rect.Top})");
        }
        else
        {
            Console.WriteLine("window: not found — MTGA must be open for real clicks");
        }

        if (!options.DryRun)
        {
            Console.WriteLine("LIVE CLICKS ENABLED — focus MTGA, menu is still manual.");
        }

        Console.WriteLine("Ctrl+C to stop.");
        Console.WriteLine();
    }

    public void OnDecision(GameView view, Intent intent)
    {
        var turn = view.Board.Turn;
        Console.WriteLine(
            $"[decision {view.Decision.DecisionId}] turn={turn.TurnNumber} phase={turn.Phase} kind={view.Decision.Kind}");
        Console.WriteLine($"  legal: {FormatLegal(view.Decision.LegalActions)}");
        Console.WriteLine($"  → {IntentFormatter.Format(intent)}");
    }

    private static string FormatLegal(IReadOnlyList<LegalAction> actions)
    {
        if (actions.Count == 0)
        {
            return "(none)";
        }

        return string.Join(", ", actions.Select(a =>
            a.InstanceId is { } id
                ? $"{ShortType(a.ActionType)}({id})"
                : ShortType(a.ActionType)));
    }

    private static string ShortType(string actionType)
    {
        const string prefix = "ActionType_";
        return actionType.StartsWith(prefix, StringComparison.Ordinal)
            ? actionType[prefix.Length..]
            : actionType;
    }

    public void OnActuate(ActuateResult result)
    {
        var status = result.Success ? "ok" : "FAIL";
        Console.WriteLine($"  actuate {status}: {result.IntentName} ({result.Actions.Count} actions)");
        if (result.Error is not null)
        {
            Console.WriteLine($"  error: {result.Error}");
        }

        if (result.Actions.Count > 0 && result.Actions.Count <= 12)
        {
            Console.WriteLine($"  ui: {UiActionFormatter.FormatAll(result.Actions)}");
        }

        Console.WriteLine();
    }

    public void OnError(string message) => Console.Error.WriteLine(message);

    public void OnInfo(string message) => Console.WriteLine(message);
}
