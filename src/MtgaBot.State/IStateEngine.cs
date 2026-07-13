using MtgaBot.Ingest;

namespace MtgaBot.State;

public interface IStateEngine
{
    event Action<GameView>? DecisionReady;

    void Apply(GreEvent evt);

    GameView? TryGetDecisionView();

    bool ActuatorBusy { get; set; }
}
