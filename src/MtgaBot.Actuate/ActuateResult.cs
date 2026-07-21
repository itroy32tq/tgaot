namespace MtgaBot.Actuate;

/// <summary>
/// Outcome of an actuate attempt. Click/hover success is <see cref="UiSucceeded"/>;
/// GRE-confirmed effect (<see cref="GreConfirmed"/>) is filled in step 1+.
/// </summary>
public enum ActuateOutcomeKind
{
    /// <summary>UI path completed (button click or hover+click). Not yet GRE-verified.</summary>
    UiSucceeded = 0,

    /// <summary>Game state confirmed the intent effect (hand left / legal changed / client submit).</summary>
    GreConfirmed = 1,

    /// <summary>Decision changed or intent no longer legal/in hand before actuation.</summary>
    StaleIntent = 2,

    /// <summary>Hand scan did not match target objectId within budget.</summary>
    HoverMiss = 3,

    /// <summary>Input/window/other hard failure.</summary>
    Failed = 4,

    /// <summary>Intentionally not executed.</summary>
    Skipped = 5,
}

public sealed record ActuateResult(
    bool Success,
    string IntentName,
    IReadOnlyList<UiAction> Actions,
    string? Error = null,
    ActuateOutcomeKind Kind = ActuateOutcomeKind.UiSucceeded,
    int? TargetInstanceId = null,
    long ElapsedMs = 0)
{
    public static ActuateResult FromKind(
        ActuateOutcomeKind kind,
        string intentName,
        IReadOnlyList<UiAction>? actions = null,
        string? error = null,
        int? targetInstanceId = null,
        long elapsedMs = 0) =>
        new(
            Success: kind is ActuateOutcomeKind.UiSucceeded
                or ActuateOutcomeKind.GreConfirmed
                or ActuateOutcomeKind.Skipped,
            IntentName: intentName,
            Actions: actions ?? [],
            Error: error,
            Kind: kind,
            TargetInstanceId: targetInstanceId,
            ElapsedMs: elapsedMs);
}
