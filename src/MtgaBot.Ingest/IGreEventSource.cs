namespace MtgaBot.Ingest;

public interface IGreEventSource
{
    IAsyncEnumerable<GreEvent> TailLive(CancellationToken ct);

    IReadOnlyList<GreEvent> ParseFile(string path);
}
