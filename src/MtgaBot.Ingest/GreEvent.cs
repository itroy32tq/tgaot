using System.Text.Json;

namespace MtgaBot.Ingest;

public sealed record GreEvent(
    ulong Sequence,
    DateTimeOffset? Timestamp,
    string RawLine,
    GreMessage Message,
    int MessageIndex);

public sealed record GreMessage(
    string Type,
    JsonElement Payload,
    GreTrafficDirection Direction);
