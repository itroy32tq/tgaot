using MtgaBot.Decide;
using MtgaBot.Ingest;

namespace MtgaBot.Host.Actuate;

public static class LiveExecuteArgs
{
    public static LiveExecuteOptions Parse(IReadOnlyList<string> args, IPlayerLogLocator? locator = null)
    {
        locator ??= new PlayerLogLocator();

        string? logPath = null;
        string? policyName = null;
        string? cardsPath = null;
        string? cardsOverlayPath = null;
        string? calibrationPath = null;
        string? attemptLogPath = null;
        var dryRun = false;
        FarmMvpMode? mode = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--log":
                    if (i + 1 >= args.Count)
                    {
                        throw new ArgumentException("Missing value for --log.");
                    }

                    logPath = args[++i];
                    break;
                case "--policy":
                    if (i + 1 >= args.Count)
                    {
                        throw new ArgumentException("Missing value for --policy.");
                    }

                    policyName = args[++i];
                    break;
                case "--mode":
                    if (i + 1 >= args.Count)
                    {
                        throw new ArgumentException("Missing value for --mode.");
                    }

                    mode = PolicyFactory.ParseMode(args[++i]);
                    break;
                case "--land-only":
                    mode = FarmMvpMode.LandOnly;
                    break;
                case "--cards":
                    if (i + 1 >= args.Count)
                    {
                        throw new ArgumentException("Missing value for --cards.");
                    }

                    cardsPath = args[++i];
                    break;
                case "--cards-overlay":
                    if (i + 1 >= args.Count)
                    {
                        throw new ArgumentException("Missing value for --cards-overlay.");
                    }

                    cardsOverlayPath = args[++i];
                    break;
                case "--calibration":
                    if (i + 1 >= args.Count)
                    {
                        throw new ArgumentException("Missing value for --calibration.");
                    }

                    calibrationPath = args[++i];
                    break;
                case "--attempt-log":
                    if (i + 1 >= args.Count)
                    {
                        throw new ArgumentException("Missing value for --attempt-log.");
                    }

                    attemptLogPath = args[++i];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--help":
                case "-h":
                    throw new ArgumentException(Usage);
                default:
                    throw new ArgumentException($"Unknown argument: {arg}\n{Usage}");
            }
        }

        logPath ??= locator.GetDefaultPlayerLogPath();
        return new LiveExecuteOptions(
            logPath,
            policyName ?? "FarmMvp",
            cardsPath,
            cardsOverlayPath,
            calibrationPath,
            dryRun,
            mode ?? FarmMvpMode.FullMvp,
            attemptLogPath);
    }

    public const string Usage =
        """
        Usage: MtgaBot.Cli actuate live [--log <path>] [--policy FarmMvp|Pass] [--mode LandOnly|LandAndCast|FullMvp] [--cards <path>] [--calibration <path>] [--attempt-log <path>] [--dry-run]

          Live in-game loop: DecisionReady → Intent → SendInput (+ hover objectId from log).
          Menu navigation is still manual (phase 3).

          --log <path>            Player.log (default: MTGA LocalLow path)
          --policy <name>         Decision policy (default: FarmMvp)
          --mode <name>           Farm capability: LandOnly | LandAndCast | FullMvp (default)
          --land-only             Shortcut for --mode LandOnly
          --cards <path>          cards.json (default: data/cards.json if present)
          --cards-overlay <path>  Optional overlay (default: data/starter_deck_cards.json)
          --calibration <path>    Calibration JSON (Lotus click_targets or compact)
          --attempt-log <path>    Append one JSON line per actuate attempt
          --dry-run               Plan actions without moving the mouse
          --help                  Show this help

        Examples:
          MtgaBot.Cli actuate live
          MtgaBot.Cli actuate live --land-only --dry-run
          MtgaBot.Cli actuate live --mode LandOnly --attempt-log attempts.jsonl
          MtgaBot.Cli actuate live --calibration path\to\calibration_config.json
        """;
}
