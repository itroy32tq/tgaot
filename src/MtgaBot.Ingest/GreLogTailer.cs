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
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        stream.Seek(0, SeekOrigin.End);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                await RecoverFromEofAsync(stream, reader, path, ct);
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

        // MTGA keeps Player.log open for write; allow shared read for replay/diagnostics.
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return _parser.ParseLines(lines);
    }

    private static async Task RecoverFromEofAsync(
        FileStream stream,
        StreamReader reader,
        string path,
        CancellationToken ct)
    {
        await Task.Delay(PollInterval, ct);

        try
        {
            // Refresh length after writers append / truncate.
            stream.Flush();
        }
        catch (IOException)
        {
            // Ignore; length check below still works for open streams.
        }

        var length = stream.Length;
        var position = stream.Position;

        if (length < position)
        {
            // Player.log was rotated/truncated (new MTGA session).
            stream.Seek(0, SeekOrigin.Begin);
            reader.DiscardBufferedData();
            return;
        }

        if (length > position)
        {
            // File grew, but StreamReader cached EOF — force re-read from Position.
            reader.DiscardBufferedData();
            return;
        }

        // Handle rare case where OS reports same size but underlying inode changed.
        var info = new FileInfo(path);
        if (info.Exists && info.Length != length)
        {
            stream.Seek(0, SeekOrigin.End);
            reader.DiscardBufferedData();
        }
    }
}
