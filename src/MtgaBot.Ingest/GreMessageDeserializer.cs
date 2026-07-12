using System.Text.Json;

namespace MtgaBot.Ingest;

public sealed class GreMessageDeserializer
{
    public IReadOnlyList<GreMessage> DeserializeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Array.Empty<GreMessage>();
        }

        var jsonStart = line.IndexOf('{');
        if (jsonStart < 0)
        {
            return Array.Empty<GreMessage>();
        }

        try
        {
            using var document = JsonDocument.Parse(line[jsonStart..]);
            return ExtractMessages(document.RootElement);
        }
        catch (JsonException)
        {
            return Array.Empty<GreMessage>();
        }
    }

    private static IReadOnlyList<GreMessage> ExtractMessages(JsonElement root)
    {
        var messages = new List<GreMessage>();

        if (root.TryGetProperty("greToClientEvent", out var greToClient)
            && greToClient.TryGetProperty("greToClientMessages", out var greMessages))
        {
            AppendMessages(messages, greMessages, GreTrafficDirection.GreToClient);
        }

        if (root.TryGetProperty("clientToGreEvent", out var clientToGre)
            && clientToGre.TryGetProperty("clientToGreMessages", out var clientMessages))
        {
            AppendMessages(messages, clientMessages, GreTrafficDirection.ClientToGre);
        }

        if (root.TryGetProperty("matchGameRoomStateChangedEvent", out var matchState))
        {
            messages.Add(new GreMessage(
                "MatchGameRoomStateChanged",
                matchState.Clone(),
                GreTrafficDirection.MatchState));
        }

        return messages;
    }

    private static void AppendMessages(
        List<GreMessage> messages,
        JsonElement messageArray,
        GreTrafficDirection direction)
    {
        foreach (var message in messageArray.EnumerateArray())
        {
            var type = message.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString() ?? string.Empty
                : string.Empty;

            messages.Add(new GreMessage(type, message.Clone(), direction));
        }
    }
}
