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
                case "--help":
                case "-h":
                    throw new ArgumentException(Usage);
                default:
                    throw new ArgumentException($"Unknown argument: {arg}\n{Usage}");
            }
        }

        logPath ??= locator.GetDefaultPlayerLogPath();
        return new ShadowOptions(logPath, follow, policyName ?? "FarmMvp");
    }

    public const string Usage =
        """
        Usage: MtgaBot.Cli shadow [--log <path>] [--follow] [--policy FarmMvp|Pass]

          --log <path>     Player.log path (default: MTGA LocalLow path)
          --follow         Tail live from end of file (no clicks)
          --policy <name>  Decision policy (default: FarmMvp)
          --help           Show this help

        Replay (Ingest + State + Decide, print DecisionReady + Intent):
          MtgaBot.Cli shadow --log path\to\Player.log --policy FarmMvp

        Live tail during a manual match:
          MtgaBot.Cli shadow --follow
          MtgaBot.Cli shadow --follow --policy Pass
        """;
}
