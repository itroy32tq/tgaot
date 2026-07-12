namespace MtgaBot.Ingest;

public sealed class GreLogTailer : IGreEventSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IPlayerLogLocator _locator;
    private readonly GreLogParser _parser;

    public GreLogTailer()
        : this(new PlayerLogLocator(), new GreLogParser())
    {
    }

    public GreLogTailer(IPlayerLogLocator locator, GreLogParser parser)
    {
        _locator = locator;
        _parser = parser;
    }

    public string DefaultLogPath => _locator.GetDefaultPlayerLogPath();

    public async IAsyncEnumerable<GreEvent> TailLive(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var greEvent in TailLive(DefaultLogPath, ct))
        {
            yield return greEvent;
        }
    }

    public async IAsyncEnumerable<GreEvent> TailLive(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Player.log not found.", path);
        }

        ulong sequence = 0;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        stream.Seek(0, SeekOrigin.End);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                await Task.Delay(PollInterval, ct);
                continue;
            }

            foreach (var greEvent in _parser.ParseLines([line]))
            {
                sequence++;
                yield return greEvent with { Sequence = sequence };
            }
        }
    }

    public IReadOnlyList<GreEvent> ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Player.log not found.", path);
        }

        return _parser.ParseLines(File.ReadLines(path));
    }
}
