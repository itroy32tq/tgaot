using MtgaBot.Decide;

namespace MtgaBot.Actuate;

public interface IIntentExecutor
{
    Task<ActuateResult> ExecuteAsync(Intent intent, CancellationToken ct);
}
