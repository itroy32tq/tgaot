namespace MtgaBot.Decide;

public sealed class EmptyCardDatabase : ICardDatabase
{
    public static EmptyCardDatabase Instance { get; } = new();

    public bool TryGet(int grpId, out CardInfo card)
    {
        card = null!;
        return false;
    }
}
