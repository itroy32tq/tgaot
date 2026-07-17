using System.Text.Json;
using System.Text.RegularExpressions;

namespace MtgaBot.Ingest;

/// <summary>
/// Extracts hover <c>objectId</c> from MTGA Player.log lines (UI hover / fragments).
/// Prefer <c>uiMessage.hover.objectId</c>; avoid GameState payloads that contain many objectIds.
/// </summary>
public static partial class HoverObjectIdParser
{
    public static int? TryParse(string? line, int? mySeatId = null)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains("objectId", StringComparison.Ordinal))
        {
            return null;
        }

        // GameState dumps contain instance objectIds — not hover UI.
        if (line.Contains("gameStateMessage", StringComparison.Ordinal)
            || line.Contains("GameStateType_", StringComparison.Ordinal))
        {
            return null;
        }

        var jsonStart = line.IndexOf('{');
        if (jsonStart >= 0)
        {
            try
            {
                using var document = JsonDocument.Parse(line[jsonStart..]);
                var root = document.RootElement;
                if (root.TryGetProperty("greToClientEvent", out _))
                {
                    // Structured GRE line: only accept uiMessage.hover (seat-filtered).
                    // Do not regex-fallback — opponent hovers / other payloads also have objectId.
                    return TryParseUiHover(root, mySeatId);
                }
            }
            catch (JsonException)
            {
                // fall through to regex
            }
        }

        var match = ObjectIdRegex().Match(line);
        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    private static int? TryParseUiHover(JsonElement root, int? mySeatId)
    {
        if (!root.TryGetProperty("greToClientEvent", out var gre)
            || !gre.TryGetProperty("greToClientMessages", out var messages)
            || messages.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var msg in messages.EnumerateArray())
        {
            if (!msg.TryGetProperty("uiMessage", out var ui))
            {
                continue;
            }

            if (mySeatId is { } seat
                && ui.TryGetProperty("seatIds", out var seats)
                && seats.ValueKind == JsonValueKind.Array)
            {
                var matched = false;
                foreach (var s in seats.EnumerateArray())
                {
                    if (!s.TryGetInt32(out var sid) || sid != seat) continue;
                    matched = true;
                    break;
                }

                if (!matched)
                {
                    continue;
                }
            }

            if (ui.TryGetProperty("hover", out var hover)
                && hover.TryGetProperty("objectId", out var objectId)
                && objectId.TryGetInt32(out var id))
            {
                return id;
            }
        }

        return null;
    }

    [GeneratedRegex("\"objectId\"\\s*:\\s*(\\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectIdRegex();
}
