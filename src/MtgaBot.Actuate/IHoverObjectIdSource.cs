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

    /// <summary>Wait until any hover objectId is observed; null on timeout.</summary>
    Task<int?> WaitForAnyAsync(TimeSpan timeout, CancellationToken ct);
}

/// <summary>
/// Test double: every point reports <see cref="HoverId"/> (default 1).
/// <see cref="WaitForAsync"/> always succeeds (cast path).
/// </summary>
public sealed class ImmediateHoverObjectIdSource(int hoverId = 1) : IHoverObjectIdSource
{
    public int HoverId { get; } = hoverId;

    public void Reset()
    {
    }

    public Task<bool> WaitForAsync(int instanceId, TimeSpan timeout, CancellationToken ct)
    {
        _ = instanceId;
        _ = timeout;
        _ = ct;
        return Task.FromResult(true);
    }

    public Task<int?> WaitForAnyAsync(TimeSpan timeout, CancellationToken ct)
    {
        _ = timeout;
        _ = ct;
        return Task.FromResult<int?>(HoverId);
    }
}

public sealed class NeverHoverObjectIdSource : IHoverObjectIdSource
{
    public void Reset()
    {
    }

    public async Task<bool> WaitForAsync(int instanceId, TimeSpan timeout, CancellationToken ct)
    {
        _ = instanceId;
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

    public async Task<int?> WaitForAnyAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await Task.Delay(timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // timeout path
        }

        return null;
    }
}
