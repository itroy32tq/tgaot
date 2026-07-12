namespace MtgaBot.Ingest;

public sealed class GreLogTailer : IGreEventSource
{
    public async IAsyncEnumerable<GreEvent> TailLive(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    public IReadOnlyList<GreEvent> ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Player.log not found.", path);
        }

        return Array.Empty<GreEvent>();
    }
}
