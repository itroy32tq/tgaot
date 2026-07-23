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
    void OnAttempt(ActuateAttemptLog attempt);
    void OnError(string message);
    void OnInfo(string message);
}

public sealed class LiveExecuteConsoleReporter : ILiveExecuteReporter
{
    private readonly string? _attemptLogPath;

    public LiveExecuteConsoleReporter(string? attemptLogPath = null)
    {
        _attemptLogPath = attemptLogPath;
    }

    public void OnStarted(
        LiveExecuteOptions options,
        CardDatabaseResolver.ResolveResult cards,
        WindowRect? window,
        string? calibrationPath)
    {
        Console.WriteLine(
            $"actuate live  dry-run={options.DryRun}  policy={options.PolicyName}  mode={options.Mode}");
        Console.WriteLine($"log: {options.LogPath}");
        Console.WriteLine(
            cards.Count > 0
                ? $"cards: {cards.Count} from {cards.CardsPath}"
                : "cards: empty (land-only / pass heuristics)");
        Console.WriteLine(
            calibrationPath is not null
                ? $"calibration: {calibrationPath}"
                : "calibration: in-code defaults (1920×1080)");
        if (options.AttemptLogPath is not null)
        {
            Console.WriteLine($"attempt-log: {options.AttemptLogPath}");
            Console.WriteLine($"decision-trace: {Path.ChangeExtension(options.AttemptLogPath, ".trace.txt")}");
            try
            {
                var dir = Path.GetDirectoryName(options.AttemptLogPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(options.AttemptLogPath, string.Empty);
                File.WriteAllText(Path.ChangeExtension(options.AttemptLogPath, ".trace.txt")!, string.Empty);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"attempt-log truncate failed: {ex.Message}");
            }
        }

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
        var ourMain1 = PriorityWindow.IsOurMain1(view.Board);
        var line =
            $"[decision {view.Decision.DecisionId}] turn={turn.TurnNumber} {turn.Phase}/{turn.Step} " +
            $"kind={view.Decision.Kind} ourMain1={ourMain1} " +
            $"active={turn.ActivePlayer} prio={turn.PriorityPlayer} me={view.Board.MySeatId} " +
            $"hand={view.Board.HandInstanceIds.Count} → {IntentFormatter.Format(intent)}";
        Console.WriteLine(line);
        Console.WriteLine($"  legal: {FormatLegal(view.Decision.LegalActions)}");
        AppendTrace(line);
    }

    private void AppendTrace(string line)
    {
        if (_attemptLogPath is null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_attemptLogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tracePath = Path.ChangeExtension(_attemptLogPath, ".trace.txt");
            File.AppendAllText(tracePath, line + Environment.NewLine);
        }
        catch
        {
            // best-effort debug trace
        }
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
        Console.WriteLine(
            $"  actuate {status}: {result.IntentName} kind={result.Kind} ({result.Actions.Count} actions, {result.ElapsedMs} ms)");
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

    public void OnAttempt(ActuateAttemptLog attempt)
    {
        Console.WriteLine(
            $"  attempt: turn={attempt.TurnNumber} {attempt.Phase}/{attempt.Step} {attempt.Intent} → {attempt.Outcome} ({attempt.ElapsedMs} ms)");

        var path = _attemptLogPath;
        if (path is null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(path, attempt.ToJsonLine() + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"attempt-log write failed: {ex.Message}");
        }
    }

    public void OnError(string message) => Console.Error.WriteLine(message);

    public void OnInfo(string message) => Console.WriteLine(message);
}
