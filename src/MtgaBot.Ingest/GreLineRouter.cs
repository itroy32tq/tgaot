namespace MtgaBot.Ingest;

public sealed class GreLineRouter
{
    public bool IsGreLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return line.Contains("greToClientEvent", StringComparison.Ordinal)
            || line.Contains("clientToGreEvent", StringComparison.Ordinal)
            || line.Contains("matchGameRoomStateChangedEvent", StringComparison.Ordinal);
    }

    public IReadOnlyList<string> MatchPatterns(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Array.Empty<string>();
        }

        var matches = new List<string>();
        foreach (var pattern in GreLogPatterns.All)
        {
            if (line.Contains(pattern, StringComparison.Ordinal))
            {
                matches.Add(pattern);
            }
        }

        return matches;
    }
}
