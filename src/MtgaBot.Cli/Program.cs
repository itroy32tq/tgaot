using MtgaBot.Host;

if (args.Length > 0 && args[0] is "shadow")
{
    Console.WriteLine($"MtgaBot shadow mode ({MtgaBotHost.Version}) — not implemented yet.");
    return 0;
}

Console.WriteLine($"MtgaBot CLI ({MtgaBotHost.Version})");
Console.WriteLine("Usage: MtgaBot.Cli shadow --log <path> [--follow]");
return 0;
