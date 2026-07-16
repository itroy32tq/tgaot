using System.Text.Json;

namespace MtgaBot.Decide;

/// <summary>
/// Loads Burning Lotus / MTGA export card DBs.
/// Supports JSON array (cards.json) and object-map (starter_deck_cards.json).
/// </summary>
public sealed class JsonCardDatabase : ICardDatabase
{
    private readonly Dictionary<int, CardInfo> _cards;

    public JsonCardDatabase(IEnumerable<CardInfo> cards)
    {
        _cards = cards.ToDictionary(card => card.GrpId);
    }

    public int Count => _cards.Count;

    public bool TryGet(int grpId, out CardInfo card) => _cards.TryGetValue(grpId, out card!);

    public static JsonCardDatabase Load(string path, string? overlayPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("cards.json not found.", path);
        }

        using var stream = File.OpenRead(path);
        var db = Load(stream);

        if (string.IsNullOrWhiteSpace(overlayPath) || !File.Exists(overlayPath)) return db;
        using var overlayStream = File.OpenRead(overlayPath);
        db = db.WithOverlay(Load(overlayStream));

        return db;
    }

    public static JsonCardDatabase Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var cards = new List<CardInfo>();

        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var element in root.EnumerateArray())
                {
                    if (TryParseCard(element, out var card))
                    {
                        cards.Add(card);
                    }
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in root.EnumerateObject())
                {
                    int? fallbackGrp = int.TryParse(property.Name, out var fromKey) ? fromKey : null;
                    if (TryParseCard(property.Value, out var card, fallbackGrp))
                    {
                        cards.Add(card);
                    }
                }

                break;

            default:
                throw new InvalidDataException(
                    $"Unsupported cards.json root kind: {root.ValueKind}. Expected array or object.");
        }

        return new JsonCardDatabase(Deduplicate(cards));
    }

    /// <summary>Overlay entries win on grpId collision (e.g. starter oracle text).</summary>
    public JsonCardDatabase WithOverlay(JsonCardDatabase overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var merged = new Dictionary<int, CardInfo>(_cards);
        foreach (var (grpId, card) in overlay._cards)
        {
            merged[grpId] = card;
        }

        return new JsonCardDatabase(merged.Values);
    }

    private static IEnumerable<CardInfo> Deduplicate(IEnumerable<CardInfo> cards)
    {
        var map = new Dictionary<int, CardInfo>();
        foreach (var card in cards)
        {
            map[card.GrpId] = card;
        }

        return map.Values;
    }

    private static bool TryParseCard(JsonElement element, out CardInfo card, int? fallbackGrpId = null)
    {
        card = null!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryReadInt(element, "grpId", out var grpId))
        {
            if (fallbackGrpId is not { } fromKey)
            {
                return false;
            }

            grpId = fromKey;
        }

        var name = ReadString(element, "name") ?? $"Card#{grpId}";
        var manaCost = ReadString(element, "manaCost") ?? ReadString(element, "mana_cost");
        var oracleText = ReadString(element, "oracleText")
                         ?? ReadString(element, "oracle_text")
                         ?? string.Empty;
        var types = ReadTypes(element);

        card = new CardInfo(grpId, name, types, manaCost, oracleText);
        return true;
    }

    private static IReadOnlyList<string> ReadTypes(JsonElement element)
    {
        if (element.TryGetProperty("types", out var typesElement))
        {
            switch (typesElement.ValueKind)
            {
                case JsonValueKind.Array:
                    return typesElement.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToArray();
                case JsonValueKind.String:
                    return SplitTypeLine(typesElement.GetString());
            }
        }

        var typeLine = ReadString(element, "type_line") ?? ReadString(element, "typeLine");
        return SplitTypeLine(typeLine);
    }

    private static IReadOnlyList<string> SplitTypeLine(string? typeLine)
    {
        if (string.IsNullOrWhiteSpace(typeLine))
        {
            return [];
        }

        return typeLine
            .Replace('—', '-')
            .Replace('–', '-')
            .Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool TryReadInt(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var prop))
        {
            return false;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
        {
            return true;
        }

        return prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value);
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return prop.GetString();
    }
}
