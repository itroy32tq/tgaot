namespace MtgaBot.Ingest;

public sealed record GreEvent(
    ulong Sequence,
    DateTimeOffset Timestamp,
    string RawLine,
    GreMessage Message);

public sealed record GreMessage(string Type, string RawPayload);
