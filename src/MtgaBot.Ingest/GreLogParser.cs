using System.Text.Json;

namespace MtgaBot.Ingest;

public sealed class GreLogParser
{
    private readonly GreLineRouter _router;
    private readonly GreMessageDeserializer _deserializer;

    public GreLogParser()
        : this(new GreLineRouter(), new GreMessageDeserializer())
    {
    }

    public GreLogParser(GreLineRouter router, GreMessageDeserializer deserializer)
    {
        _router = router;
        _deserializer = deserializer;
    }

    public IReadOnlyList<GreEvent> ParseLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var events = new List<GreEvent>();
        ulong sequence = 0;

        foreach (var line in lines)
        {
            if (!_router.IsGreLine(line))
            {
                continue;
            }

            var messages = _deserializer.DeserializeLine(line);
            if (messages.Count == 0)
            {
                continue;
            }

            var timestamp = TryParseTimestamp(line);
            for (var index = 0; index < messages.Count; index++)
            {
                events.Add(new GreEvent(
                    ++sequence,
                    timestamp,
                    line,
                    messages[index],
                    index));
            }
        }

        return events;
    }

    internal static DateTimeOffset? TryParseTimestamp(string line)
    {
        var jsonStart = line.IndexOf('{');
        if (jsonStart < 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line[jsonStart..]);
            if (!document.RootElement.TryGetProperty("timestamp", out var timestampElement))
            {
                return null;
            }

            if (timestampElement.ValueKind == JsonValueKind.String
                && long.TryParse(timestampElement.GetString(), out var epochMs))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
            }

            if (timestampElement.ValueKind == JsonValueKind.Number
                && timestampElement.TryGetInt64(out epochMs))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(epochMs);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
