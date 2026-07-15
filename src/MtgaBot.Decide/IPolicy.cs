using MtgaBot.State;

namespace MtgaBot.Decide;

public interface IPolicy
{
    Intent Decide(GameView view, ICardDatabase cards);
}
