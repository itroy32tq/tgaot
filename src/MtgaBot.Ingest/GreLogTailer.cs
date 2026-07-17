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
        ulong sequence = 0;
        await foreach (var line in TailRawLines(path, ct))
        {
            foreach (var greEvent in _parser.ParseLines([line]))
            {
                sequence++;
                yield return greEvent with { Sequence = sequence };
            }
        }
    }

    /// <summary>
    /// Live-tail raw log lines. When <paramref name="startOffset"/> is set, resume from that
    /// byte position; otherwise seek to EOF.
    /// </summary>
    public async IAsyncEnumerable<string> TailRawLines(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
        long? startOffset = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Player.log not found.", path);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        if (startOffset is { } offset)
        {
            stream.Seek(Math.Clamp(offset, 0, stream.Length), SeekOrigin.Begin);
        }
        else
        {
            stream.Seek(0, SeekOrigin.End);
        }

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                await RecoverFromEofAsync(stream, reader, path, ct);
                continue;
            }

            yield return line;
        }
    }

    /// <summary>
    /// Parse the trailing portion of Player.log so live mode can catch a prompt already on screen
    /// (e.g. mulligan) that was written before the process started.
    /// </summary>
    public RecentLogParseResult ParseRecent(string path, long maxBytes = 2_000_000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Player.log not found.", path);
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var length = stream.Length;
        var start = Math.Max(0L, length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);

        var toRead = (int)Math.Min(int.MaxValue, length - start);
        var buffer = new byte[toRead];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        if (start > 0)
        {
            var nl = text.IndexOf('\n');
            if (nl >= 0 && nl + 1 < text.Length)
            {
                text = text[(nl + 1)..];
            }
            else if (nl >= 0)
            {
                text = string.Empty;
            }
        }

        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        var events = _parser.ParseLines(lines);
        var resumeOffset = start + read;
        return new RecentLogParseResult(events, resumeOffset, read);
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
