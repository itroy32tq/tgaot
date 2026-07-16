namespace MtgaBot.Actuate;

/// <summary>
/// Shared flag for DecisionGate coordination (Host reads this while Actuate runs).
/// </summary>
public sealed class ActuatorBusy
{
    private int _busy;

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public IDisposable Enter()
    {
        Interlocked.Exchange(ref _busy, 1);
        return new Releaser(this);
    }

    private void Exit() => Interlocked.Exchange(ref _busy, 0);

    private sealed class Releaser(ActuatorBusy owner) : IDisposable
    {
        public void Dispose() => owner.Exit();
    }
}
