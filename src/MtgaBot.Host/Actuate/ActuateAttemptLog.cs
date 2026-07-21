using System.Text.Json;
using System.Text.Json.Serialization;
using MtgaBot.Actuate;
using MtgaBot.Decide;
using MtgaBot.State;

namespace MtgaBot.Host.Actuate;

/// <summary>One actuate attempt — console + optional jsonl.</summary>
public sealed record ActuateAttemptLog(
    ulong DecisionId,
    int TurnNumber,
    string Phase,
    string Step,
    string Intent,
    int? TargetInstanceId,
    string Outcome,
    long ElapsedMs,
    string? Error = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ActuateAttemptLog From(
        GameView view,
        Intent intent,
        ActuateResult result) =>
        new(
            view.Decision.DecisionId,
            view.Board.Turn.TurnNumber,
            view.Board.Turn.Phase,
            view.Board.Turn.Step,
            IntentFormatter.Format(intent),
            result.TargetInstanceId ?? IntentPreflight.GetHandTarget(intent),
            result.Kind.ToString(),
            result.ElapsedMs,
            result.Error);

    public string ToJsonLine() => JsonSerializer.Serialize(this, JsonOptions);
}
