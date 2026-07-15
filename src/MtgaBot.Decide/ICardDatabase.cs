namespace MtgaBot.Decide;

public interface ICardDatabase
{
    bool TryGet(int grpId, out CardInfo card);
}
