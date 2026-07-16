namespace MtgaBot.Decide;

/// <summary>
/// Resolves cards.json (+ optional starter overlay) for CLI / Host.
/// </summary>
public static class CardDatabaseResolver
{
    public const string DefaultRelativePath = "data/cards.json";
    public const string DefaultOverlayRelativePath = "data/starter_deck_cards.json";

    public sealed record ResolveResult(
        ICardDatabase Database,
        string? CardsPath,
        string? OverlayPath,
        int Count);

    /// <summary>
    /// Loads cards from <paramref name="explicitPath"/> or default <c>data/cards.json</c>.
    /// Missing default file → <see cref="EmptyCardDatabase"/> (no throw).
    /// Explicit path that does not exist → throws <see cref="FileNotFoundException"/>.
    /// Overlay: explicit path, else <c>starter_deck_cards.json</c> next to cards,
    /// else default <c>data/starter_deck_cards.json</c> only when using the default cards path.
    /// </summary>
    public static ResolveResult Resolve(string? explicitPath = null, string? overlayPath = null)
    {
        var usedExplicitCards = !string.IsNullOrWhiteSpace(explicitPath);
        var cardsPath = ResolveCardsPath(explicitPath);
        if (cardsPath is null)
        {
            return new ResolveResult(EmptyCardDatabase.Instance, null, null, 0);
        }

        var overlay = ResolveOverlayPath(
            overlayPath,
            Path.GetDirectoryName(cardsPath),
            searchDefaultTree: !usedExplicitCards);
        var db = JsonCardDatabase.Load(cardsPath, overlay);
        return new ResolveResult(db, cardsPath, overlay, db.Count);
    }

    public static string? ResolveCardsPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);
            if (!File.Exists(full))
            {
                throw new FileNotFoundException("cards.json not found.", full);
            }

            return full;
        }

        foreach (var candidate in EnumerateDefaultCandidates(DefaultRelativePath))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? ResolveOverlayPath(
        string? explicitOverlay,
        string? cardsDirectory,
        bool searchDefaultTree = true)
    {
        if (!string.IsNullOrWhiteSpace(explicitOverlay))
        {
            var full = Path.GetFullPath(explicitOverlay);
            return File.Exists(full) ? full : null;
        }

        if (!string.IsNullOrWhiteSpace(cardsDirectory))
        {
            var sibling = Path.Combine(cardsDirectory, "starter_deck_cards.json");
            if (File.Exists(sibling))
            {
                return Path.GetFullPath(sibling);
            }
        }

        if (!searchDefaultTree)
        {
            return null;
        }

        foreach (var candidate in EnumerateDefaultCandidates(DefaultOverlayRelativePath))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateDefaultCandidates(string relativePath)
    {
        yield return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativePath));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            yield return Path.GetFullPath(Path.Combine(dir.FullName, relativePath));
        }
    }
}
