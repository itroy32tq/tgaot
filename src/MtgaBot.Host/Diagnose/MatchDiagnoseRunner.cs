using System.Text.Json;
using MtgaBot.Decide;
using MtgaBot.Ingest;
using MtgaBot.State;

namespace MtgaBot.Host.Diagnose;

public sealed record MatchDiagnoseOptions(
    string LogPath,
    long MaxBytes = 3_000_000,
    FarmMvpMode Mode = FarmMvpMode.LandOnly,
    bool AllMatches = false);

public static class MatchDiagnoseArgs
{
    public static MatchDiagnoseOptions Parse(string[] args)
    {
        string? log = null;
        long maxBytes = 3_000_000;
        var mode = FarmMvpMode.LandOnly;
        var allMatches = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--log":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("Missing value for --log.");
                    }

                    log = args[++i];
                    break;
                case "--bytes":
                    if (i + 1 >= args.Length || !long.TryParse(args[++i], out maxBytes))
                    {
                        throw new ArgumentException("Missing/invalid value for --bytes.");
                    }

                    break;
                case "--mode":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("Missing value for --mode.");
                    }

                    mode = PolicyFactory.ParseMode(args[++i]);
                    break;
                case "--all-matches":
                    allMatches = true;
                    break;
                case "--help" or "-h":
                    throw new ArgumentException(Usage);
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}\n{Usage}");
            }
        }

        log ??= new PlayerLogLocator().GetDefaultPlayerLogPath();
        return new MatchDiagnoseOptions(log, maxBytes, mode, allMatches);
    }

    public const string Usage =
        """
        Usage: MtgaBot.Cli diagnose match [--log <path>] [--bytes <n>] [--mode LandOnly|LandAndCast|FullMvp] [--all-matches]

          Replays the tail of Player.log through State + FarmMvp and prints a turn/decision timeline.
          By default only the last match in the tail is shown (use --all-matches for the whole tail).
          Use after a live run to see why PlayLand was skipped (priority, active seat, land-once).
        """;
}

/// <summary>
/// Offline replay of recent GRE into StateEngine + FarmMvp — no mouse, no live tail.
/// </summary>
public sealed class MatchDiagnoseRunner
{
    public void Run(MatchDiagnoseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(options.LogPath))
        {
            throw new FileNotFoundException("Player.log not found.", options.LogPath);
        }

        var tailer = new GreLogTailer();
        var recent = tailer.ParseRecent(options.LogPath, options.MaxBytes);
        var events = options.AllMatches
            ? recent.Events
            : SliceLastMatch(recent.Events);

        var engine = new StateEngine();
        var policy = new FarmMvpPolicy(mode: options.Mode);
        var cards = CardDatabaseResolver.Resolve(null, null).Database;

        Console.WriteLine($"diagnose match  mode={options.Mode}  scope={(options.AllMatches ? "all" : "last-match")}");
        Console.WriteLine($"log: {options.LogPath}");
        Console.WriteLine(
            $"parsed: {events.Count} GRE events" +
            (options.AllMatches
                ? $" from last {recent.BytesRead / 1024} KB"
                : $" (last match; tail had {recent.Events.Count} events / {recent.BytesRead / 1024} KB)"));
        Console.WriteLine();

        string? lastTurnKey = null;
        var decisions = 0;
        var playLandIntents = 0;
        var ourMain1WithPlay = 0;
        var ourMain1Skipped = 0;
        var ourMain1PlayLand = 0;
        var lines = new List<string>();
        var landWindows = new List<string>();

        engine.DecisionReady += view =>
        {
            decisions++;
            var turn = view.Board.Turn;
            var intent = policy.Decide(view, cards);
            if (intent is PlayLandIntent)
            {
                // Mirror live: settle after choosing PlayLand so timeline is once-per-turn.
                policy.NotifyLandActuateStarted();
            }
            var playLegal = view.Decision.LegalActions.Count(a =>
                a.ActionType.Contains("Play", StringComparison.OrdinalIgnoreCase)
                && a.InstanceId is not null);
            var ourMain1 = PriorityWindow.IsOurMain1(view.Board);
            var intentName = intent.GetType().Name;
            if (intent is PlayLandIntent play)
            {
                playLandIntents++;
                intentName = $"PlayLandIntent({play.InstanceId})";
            }
            else if (intent is CastIntent cast)
            {
                intentName = $"CastIntent({cast.InstanceId})";
            }
            else if (intent is NoOpIntent nop)
            {
                intentName = $"NoOp({nop.Reason})";
            }

            if (ourMain1 && playLegal > 0 && view.Decision.Kind == DecisionKind.MainPhase)
            {
                ourMain1WithPlay++;
                if (intent is PlayLandIntent)
                {
                    ourMain1PlayLand++;
                    landWindows.Add(
                        $"T{turn.TurnNumber} Main1 me={view.Board.MySeatId} → {intentName} " +
                        $"(hand={view.Board.HandInstanceIds.Count} playLegal={playLegal})");
                }
                else
                {
                    ourMain1Skipped++;
                    landWindows.Add(
                        $"T{turn.TurnNumber} Main1 me={view.Board.MySeatId} → SKIPPED ({intentName}) " +
                        $"playLegal={playLegal}");
                }
            }

            var legalPreview = string.Join(
                ", ",
                view.Decision.LegalActions
                    .Take(8)
                    .Select(a => a.InstanceId is { } id
                        ? $"{ShortAction(a.ActionType)}({id})"
                        : ShortAction(a.ActionType)));
            if (view.Decision.LegalActions.Count > 8)
            {
                legalPreview += ", …";
            }

            lines.Add(
                $"[d{view.Decision.DecisionId}] turn={turn.TurnNumber} {turn.Phase}/{turn.Step} " +
                $"kind={view.Decision.Kind} ourMain1={ourMain1} " +
                $"prio={turn.PriorityPlayer} active={turn.ActivePlayer} decision={turn.DecisionPlayer} me={view.Board.MySeatId} " +
                $"playLegal={playLegal} hand={view.Board.HandInstanceIds.Count} pending={view.Board.PendingMessageCount}");
            lines.Add($"  legal: {legalPreview}");
            lines.Add($"  → {intentName}");
            if (view.Decision.Kind == DecisionKind.MainPhase
                && turn.Phase == "Phase_Main1"
                && playLegal > 0
                && intent is not PlayLandIntent)
            {
                lines.Add($"  !! PlayLand SKIPPED: {ExplainSkippedLand(view, ourMain1, intent)}");
            }

            if (PriorityWindow.IsOurTurnMain1(view.Board)
                && playLegal > 0
                && intent is PassPriorityIntent
                && !ourMain1
                && !policy.LandSettledThisTurn)
            {
                lines.Add(
                    "  !! BAD: Pass on our Main1 without priority — burns land window " +
                    $"(active={turn.ActivePlayer} prio={turn.PriorityPlayer} me={view.Board.MySeatId})");
            }

            lines.Add("");
        };

        ulong seq = 0;
        foreach (var evt in events)
        {
            seq++;
            engine.Apply(evt with { Sequence = seq });
            if (engine.TryGetSnapshot() is not { } snap)
            {
                continue;
            }

            var key =
                $"{snap.Turn.TurnNumber}|{snap.Turn.Phase}|{snap.Turn.Step}|{snap.Turn.ActivePlayer}|{snap.Turn.PriorityPlayer}";
            if (!string.Equals(key, lastTurnKey, StringComparison.Ordinal))
            {
                lastTurnKey = key;
                if (snap.Turn.TurnNumber > 0
                    && snap.Turn.Phase is "Phase_Main1" or "Phase_Main2" or "Phase_Beginning"
                        or "Phase_Combat" or "Phase_Ending")
                {
                    lines.Add(
                        $"-- GRE turn={snap.Turn.TurnNumber} {snap.Turn.Phase}/{snap.Turn.Step} " +
                        $"active={snap.Turn.ActivePlayer} prio={snap.Turn.PriorityPlayer} " +
                        $"ourMain1={PriorityWindow.IsOurMain1(snap)} me={snap.MySeatId} " +
                        $"hand={snap.HandInstanceIds.Count}");
                }
            }
        }

        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine("--- land windows (our Main1 + Play legal) ---");
        if (landWindows.Count == 0)
        {
            Console.WriteLine("(none)");
        }
        else
        {
            // Deduplicate consecutive identical summaries (sticky prompt spam).
            string? prev = null;
            foreach (var w in landWindows)
            {
                if (string.Equals(w, prev, StringComparison.Ordinal))
                {
                    continue;
                }

                Console.WriteLine(w);
                prev = w;
            }
        }

        Console.WriteLine();
        Console.WriteLine("--- summary ---");
        Console.WriteLine($"decisions emitted:          {decisions}");
        Console.WriteLine($"PlayLand intents (any):     {playLandIntents}");
        Console.WriteLine($"Our Main1 + Play legal:     {ourMain1WithPlay}");
        Console.WriteLine($"  → PlayLand chosen:        {ourMain1PlayLand}");
        Console.WriteLine($"  → PlayLand skipped:       {ourMain1Skipped}");
        if (ourMain1Skipped > 0)
        {
            Console.WriteLine("Look for '!! PlayLand SKIPPED' on our Main1 lines above.");
        }
    }

    /// <summary>
    /// Keep events from the last GameStateType_Full (or MatchInProgress) boundary to EOF.
    /// </summary>
    internal static IReadOnlyList<GreEvent> SliceLastMatch(IReadOnlyList<GreEvent> events)
    {
        var start = 0;
        for (var i = 0; i < events.Count; i++)
        {
            if (IsMatchStart(events[i]))
            {
                start = i;
            }
        }

        if (start == 0)
        {
            return events;
        }

        return events.Skip(start).ToList();
    }

    private static bool IsMatchStart(GreEvent evt)
    {
        var message = evt.Message;
        if (string.Equals(message.Type, "MatchGameRoomStateChanged", StringComparison.Ordinal))
        {
            // Payload is matchGameRoomStateChangedEvent body.
            if (TryGetString(message.Payload, "gameRoomInfo", "stateType", out var state)
                && state is "MatchGameRoomStateType_Playing" or "MatchGameRoomStateType_MatchInProgress")
            {
                return true;
            }
        }

        // GRE envelope: { type, gameStateMessage: { type: GameStateType_Full, ... } }
        if (message.Payload.TryGetProperty("gameStateMessage", out var gsm)
            && gsm.TryGetProperty("type", out var gsmType)
            && gsmType.GetString() == "GameStateType_Full")
        {
            return true;
        }

        return false;
    }

    private static bool TryGetString(JsonElement root, string a, string b, out string value)
    {
        value = "";
        if (!root.TryGetProperty(a, out var mid) || !mid.TryGetProperty(b, out var leaf))
        {
            return false;
        }

        if (leaf.GetString() is not { } s)
        {
            return false;
        }

        value = s;
        return true;
    }

    private static string ShortAction(string actionType)
    {
        if (actionType.StartsWith("ActionType_", StringComparison.Ordinal))
        {
            return actionType["ActionType_".Length..];
        }

        return actionType;
    }

    private static string ExplainSkippedLand(GameView view, bool ourMain1, Intent intent)
    {
        var t = view.Board.Turn;
        var me = view.Board.MySeatId;
        if (!ourMain1)
        {
            if (t.Phase == "Phase_Main1" && t.ActivePlayer > 0 && t.ActivePlayer != me)
            {
                return $"opponent Main1 (active={t.ActivePlayer} prio={t.PriorityPlayer} me={me}) — Pass expected";
            }

            return $"not our Main1 window (phase={t.Phase} active={t.ActivePlayer} prio={t.PriorityPlayer} me={me})";
        }

        if (intent is PassPriorityIntent)
        {
            return "policy returned Pass (land settled this turn, or LandOnly after land)";
        }

        return $"policy returned {intent.GetType().Name}";
    }
}
