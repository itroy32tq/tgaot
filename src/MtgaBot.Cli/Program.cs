using MtgaBot.Actuate;
using MtgaBot.Host.Actuate;
using MtgaBot.Host.Shadow;

const string ActuateUsage =
    """
    Usage:
      MtgaBot.Cli actuate dry-run --intent <name> [--instance <id>] [--calibration <path>]
      MtgaBot.Cli actuate live [options]
      MtgaBot.Cli actuate mouse-probe

      dry-run      Plans UiAction sequence without moving the mouse.
      live         Follow Player.log and execute Intents in MTGA (phase 3 in-game).
      mouse-probe  Move cursor +40,+20 and verify Win32 input works.

      Intents: pass, resolve, attack, keep, mulligan, noblocks, group, cast, target

      Examples:
        MtgaBot.Cli actuate mouse-probe
        MtgaBot.Cli actuate dry-run --intent pass
        MtgaBot.Cli actuate live --dry-run
        MtgaBot.Cli actuate live
    """;

if (args.Length == 0)
{
    PrintRootHelp();
    return 0;
}

switch (args[0])
{
    case "shadow":
        return await RunShadowAsync(args[1..]);
    case "actuate":
        return await RunActuateAsync(args[1..]);
    case "--help" or "-h" or "help":
        PrintRootHelp();
        return 0;
}

Console.Error.WriteLine($"Unknown command: {args[0]}");
PrintRootHelp();
return 1;

static async Task<int> RunShadowAsync(string[] shadowArgs)
{
    ShadowOptions options;
    try
    {
        options = ShadowArgs.Parse(shadowArgs);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var reporter = new ShadowConsoleReporter();
    var runner = new ShadowRunner(reporter, options);

    try
    {
        await runner.RunAsync(options, cts.Token);
        return 0;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine();
        Console.WriteLine("stopped.");
        return 0;
    }
    catch (FileNotFoundException)
    {
        return 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"shadow failed: {ex.Message}");
        return 1;
    }
}

static async Task<int> RunActuateAsync(string[] actuateArgs)
{
    if (actuateArgs.Length == 0 || actuateArgs[0] is "--help" or "-h")
    {
        Console.WriteLine(ActuateUsage);
        Console.WriteLine();
        Console.WriteLine(LiveExecuteArgs.Usage);
        return actuateArgs.Length == 0 ? 1 : 0;
    }

    if (string.Equals(actuateArgs[0], "live", StringComparison.OrdinalIgnoreCase))
    {
        return await RunActuateLiveAsync(actuateArgs[1..]);
    }

    if (string.Equals(actuateArgs[0], "mouse-probe", StringComparison.OrdinalIgnoreCase))
    {
        return await MouseProbe.RunAsync(Console.Out);
    }

    if (!string.Equals(actuateArgs[0], "dry-run", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Unknown actuate subcommand: {actuateArgs[0]}");
        Console.WriteLine(ActuateUsage);
        return 1;
    }

    string? intentName = null;
    int? instanceId = null;
    string? calibration = null;

    for (var i = 1; i < actuateArgs.Length; i++)
    {
        switch (actuateArgs[i])
        {
            case "--intent":
                if (i + 1 >= actuateArgs.Length)
                {
                    Console.Error.WriteLine("Missing value for --intent.");
                    return 1;
                }

                intentName = actuateArgs[++i];
                break;
            case "--instance":
                if (i + 1 >= actuateArgs.Length || !int.TryParse(actuateArgs[++i], out var id))
                {
                    Console.Error.WriteLine("Missing/invalid value for --instance.");
                    return 1;
                }

                instanceId = id;
                break;
            case "--calibration":
                if (i + 1 >= actuateArgs.Length)
                {
                    Console.Error.WriteLine("Missing value for --calibration.");
                    return 1;
                }

                calibration = actuateArgs[++i];
                break;
            default:
                Console.Error.WriteLine($"Unknown argument: {actuateArgs[i]}");
                Console.WriteLine(ActuateUsage);
                return 1;
        }
    }

    if (string.IsNullOrWhiteSpace(intentName))
    {
        Console.Error.WriteLine("Required: --intent <name>");
        Console.WriteLine(ActuateUsage);
        return 1;
    }

    try
    {
        var intent = ActuateDryRun.ParseIntent(intentName, instanceId);
        var result = await ActuateDryRun.PlanAsync(intent, calibration);
        Console.WriteLine($"intent: {result.IntentName}  success={result.Success}");
        if (result.Error is not null)
        {
            Console.WriteLine($"error: {result.Error}");
        }

        Console.WriteLine($"actions ({result.Actions.Count}): {UiActionFormatter.FormatAll(result.Actions)}");
        return result.Success ? 0 : 2;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static async Task<int> RunActuateLiveAsync(string[] liveArgs)
{
    LiveExecuteOptions options;
    try
    {
        options = LiveExecuteArgs.Parse(liveArgs);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var reporter = new LiveExecuteConsoleReporter(options.AttemptLogPath);
    var runner = new LiveExecuteRunner(reporter, options);

    try
    {
        await runner.RunAsync(options, cts.Token);
        return 0;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine();
        Console.WriteLine("stopped.");
        return 0;
    }
    catch (FileNotFoundException)
    {
        return 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"actuate live failed: {ex.Message}");
        return 1;
    }
}

static void PrintRootHelp()
{
    Console.WriteLine($"MtgaBot CLI ({MtgaBot.Host.MtgaBotHost.Version})");
    Console.WriteLine();
    Console.WriteLine(ShadowArgs.Usage);
    Console.WriteLine();
    Console.WriteLine(ActuateUsage);
}
