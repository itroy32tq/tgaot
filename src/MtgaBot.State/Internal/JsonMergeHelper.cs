using System.Text.Json;

namespace MtgaBot.State.Internal;

internal static class JsonMergeHelper
{
    public static List<Dictionary<string, JsonElement>> MergeListByKey(
        IReadOnlyList<Dictionary<string, JsonElement>> existing,
        IReadOnlyList<Dictionary<string, JsonElement>> updates,
        string keyName,
        IReadOnlyList<int>? deletedIds = null)
    {
        var merged = new Dictionary<int, Dictionary<string, JsonElement>>();
        var order = new List<int>();

        void AddItem(Dictionary<string, JsonElement> item)
        {
            if (!item.TryGetValue(keyName, out var keyElement) || keyElement.ValueKind != JsonValueKind.Number)
            {
                return;
            }

            var key = keyElement.GetInt32();
            if (!merged.ContainsKey(key))
            {
                merged[key] = new Dictionary<string, JsonElement>(item);
                order.Add(key);
                return;
            }

            foreach (var (field, value) in item)
            {
                merged[key][field] = value;
            }
        }

        foreach (var item in existing)
        {
            AddItem(item);
        }

        foreach (var item in updates)
        {
            AddItem(item);
        }

        if (deletedIds is not null)
        {
            foreach (var deletedId in deletedIds)
            {
                merged.Remove(deletedId);
            }

            order.RemoveAll(id => deletedIds.Contains(id));
        }

        return order.Where(id => merged.ContainsKey(id)).Select(id => merged[id]).ToList();
    }

    public static List<Dictionary<string, JsonElement>> ParseObjectArray(JsonElement array)
    {
        var result = new List<Dictionary<string, JsonElement>>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var dict = new Dictionary<string, JsonElement>();
            foreach (var property in item.EnumerateObject())
            {
                dict[property.Name] = property.Value.Clone();
            }

            result.Add(dict);
        }

        return result;
    }

    public static List<int> ParseIntArray(JsonElement array)
    {
        var result = new List<int>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    public static Dictionary<string, JsonElement> ParseObject(JsonElement element)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value.Clone();
        }

        return dict;
    }
}
