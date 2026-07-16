namespace MtgaBot.Actuate;

public sealed record ActuateResult(
    bool Success,
    string IntentName,
    IReadOnlyList<UiAction> Actions,
    string? Error = null);
