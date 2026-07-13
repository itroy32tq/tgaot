namespace MtgaBot.State;

public sealed record LegalAction(
    string ActionType,
    int? InstanceId,
    int SeatId,
    IReadOnlyDictionary<string, object?>? Payload);
