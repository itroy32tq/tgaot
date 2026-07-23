using MtgaBot.Actuate;
using MtgaBot.Ingest;

namespace MtgaBot.Host.Actuate;

/// <summary>
/// Thread-safe hover id source fed by raw Player.log lines (Host wires Ingest → Actuate).
/// </summary>
public sealed class LogHoverObjectIdSource : IHoverObjectIdSource
{
    private readonly object _gate = new();
    private readonly List<int> _pending = [];
    private TaskCompletionSource<int>? _waiter;
    private int? _waitTarget;
    private bool _waitAny;
    private int? _mySeatId;

    public void SetMySeatId(int? seatId) => _mySeatId = seatId;

    public void ObserveLine(string line)
    {
        var id = HoverObjectIdParser.TryParse(line, _mySeatId);
        if (id is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_waiter is not null && (_waitAny || _waitTarget == id))
            {
                var waiter = _waiter;
                _waiter = null;
                _waitTarget = null;
                _waitAny = false;
                waiter.TrySetResult(id.Value);
                return;
            }

            _pending.Add(id.Value);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _pending.Clear();
            _waiter?.TrySetCanceled();
            _waiter = null;
            _waitTarget = null;
            _waitAny = false;
        }
    }

    public async Task<bool> WaitForAsync(int instanceId, TimeSpan timeout, CancellationToken ct)
    {
        TaskCompletionSource<int> waiter;
        lock (_gate)
        {
            var idx = _pending.FindIndex(id => id == instanceId);
            if (idx >= 0)
            {
                _pending.RemoveAt(idx);
                return true;
            }

            waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiter = waiter;
            _waitTarget = instanceId;
            _waitAny = false;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        try
        {
            await using var _ = linked.Token.Register(() => waiter.TrySetCanceled(linked.Token));
            var observed = await waiter.Task.ConfigureAwait(false);
            return observed == instanceId;
        }
        catch (OperationCanceledException)
        {
            ClearWaiter(waiter);
            return false;
        }
    }

    public async Task<int?> WaitForAnyAsync(TimeSpan timeout, CancellationToken ct)
    {
        TaskCompletionSource<int> waiter;
        lock (_gate)
        {
            if (_pending.Count > 0)
            {
                // Most recent id best matches the current cursor position.
                var id = _pending[^1];
                _pending.Clear();
                return id;
            }

            waiter = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiter = waiter;
            _waitTarget = null;
            _waitAny = true;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        try
        {
            await using var _ = linked.Token.Register(() => waiter.TrySetCanceled(linked.Token));
            return await waiter.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ClearWaiter(waiter);
            return null;
        }
    }

    private void ClearWaiter(TaskCompletionSource<int> waiter)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_waiter, waiter))
            {
                _waiter = null;
                _waitTarget = null;
                _waitAny = false;
            }
        }
    }
}
