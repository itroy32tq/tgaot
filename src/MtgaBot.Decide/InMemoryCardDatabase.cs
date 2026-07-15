namespace MtgaBot.Decide;

public sealed class InMemoryCardDatabase : ICardDatabase
{
    private readonly Dictionary<int, CardInfo> _cards;

    public InMemoryCardDatabase(IEnumerable<CardInfo> cards)
    {
        _cards = cards.ToDictionary(card => card.GrpId);
    }

    public bool TryGet(int grpId, out CardInfo card) => _cards.TryGetValue(grpId, out card!);
}
