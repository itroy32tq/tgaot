using MtgaBot.Decide;
using MtgaBot.Ingest;

namespace MtgaBot.Host.Shadow;

public static class ShadowArgs
{
    public static ShadowOptions Parse(IReadOnlyList<string> args, IPlayerLogLocator? locator = null)
    {
        locator ??= new PlayerLogLocator();

        string? logPath = null;
        var follow = false;
        string? policyName = null;
        string? cardsPath = null;
        string? cardsOverlayPath = null;
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
                case "--follow":
                    follow = true;
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
                case "--help":
                case "-h":
                    throw new ArgumentException(Usage);
                default:
                    throw new ArgumentException($"Unknown argument: {arg}\n{Usage}");
            }
        }

        logPath ??= locator.GetDefaultPlayerLogPath();
        return new ShadowOptions(
            logPath,
            follow,
            policyName ?? "FarmMvp",
            cardsPath,
            cardsOverlayPath,
            mode ?? FarmMvpMode.FullMvp);
    }

    public const string Usage =
        """
        Usage: MtgaBot.Cli shadow [--log <path>] [--follow] [--policy FarmMvp|Pass] [--mode LandOnly|LandAndCast|FullMvp] [--cards <path>]

          --log <path>            Player.log path (default: MTGA LocalLow path)
          --follow                Tail live from end of file (no clicks)
          --policy <name>         Decision policy (default: FarmMvp)
          --mode <name>           Farm capability: LandOnly | LandAndCast | FullMvp (default)
          --land-only             Shortcut for --mode LandOnly
          --cards <path>          cards.json (default: data/cards.json if present)
          --cards-overlay <path>  Optional overlay (default: data/starter_deck_cards.json)
          --help                  Show this help

        Replay (Ingest + State + Decide, print DecisionReady + Intent):
          MtgaBot.Cli shadow --log path\to\Player.log --policy FarmMvp --mode LandOnly

        Live tail during a manual match:
          MtgaBot.Cli shadow --follow
          MtgaBot.Cli shadow --follow --policy Pass --cards data\cards.json
        """;
}
