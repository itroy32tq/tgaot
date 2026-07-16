namespace MtgaBot.Actuate;

public interface IInputBackend
{
    Task ExecuteAsync(UiAction action, CancellationToken ct);
}

public sealed class RecordingInputBackend : IInputBackend
{
    public List<UiAction> Actions { get; } = [];

    public Task ExecuteAsync(UiAction action, CancellationToken ct)
    {
        Actions.Add(action);
        return Task.CompletedTask;
    }
}

/// <summary>Applies actions through an inner backend; also records them for dry-run inspection.</summary>
public sealed class RecordingProxyInputBackend(IInputBackend inner) : IInputBackend
{
    public List<UiAction> Actions { get; } = [];

    public async Task ExecuteAsync(UiAction action, CancellationToken ct)
    {
        Actions.Add(action);
        await inner.ExecuteAsync(action, ct).ConfigureAwait(false);
    }
}
