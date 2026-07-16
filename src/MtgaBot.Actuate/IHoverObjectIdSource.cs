namespace MtgaBot.Actuate;

/// <summary>
/// Provides hover <c>objectId</c> observations from the log (wired by Host to Ingest).
/// Actuate does not depend on Ingest directly.
/// </summary>
public interface IHoverObjectIdSource
{
    /// <summary>Clear pending hover ids before a scan.</summary>
    void Reset();

    /// <summary>Wait until an objectId matching <paramref name="instanceId"/> is observed.</summary>
    Task<bool> WaitForAsync(int instanceId, TimeSpan timeout, CancellationToken ct);
}

/// <summary>Test double: succeeds after the given number of <see cref="Reset"/> calls… or immediately.</summary>
public sealed class ImmediateHoverObjectIdSource : IHoverObjectIdSource
{
    public void Reset()
    {
    }

    public Task<bool> WaitForAsync(int instanceId, TimeSpan timeout, CancellationToken ct) =>
        Task.FromResult(true);
}

public sealed class NeverHoverObjectIdSource : IHoverObjectIdSource
{
    public void Reset()
    {
    }

    public async Task<bool> WaitForAsync(int instanceId, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await Task.Delay(timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // timeout path
        }

        return false;
    }
}
