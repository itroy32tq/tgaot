namespace MtgaBot.Ingest;

public sealed record RecentLogParseResult(
    IReadOnlyList<GreEvent> Events,
    long ResumeOffset,
    long BytesRead);
