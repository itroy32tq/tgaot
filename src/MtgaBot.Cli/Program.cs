using MtgaBot.Host.Shadow;

if (args.Length == 0)
{
    PrintRootHelp();
    return 0;
}

switch (args[0])
{
    case "shadow":
        return await RunShadowAsync(args[1..]);
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

static void PrintRootHelp()
{
    Console.WriteLine($"MtgaBot CLI ({MtgaBot.Host.MtgaBotHost.Version})");
    Console.WriteLine();
    Console.WriteLine(ShadowArgs.Usage);
}
