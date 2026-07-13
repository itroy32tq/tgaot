using MtgaBot.State.Internal;

namespace MtgaBot.State;

internal sealed class GameSnapshotBuilder
{
    private readonly ZoneIndex _zoneIndex = new();
    private readonly ObjectRegistry _objectRegistry = new();

    public GameSnapshot Build(MutableGameState state, int mySeatId)
    {
        var objects = _objectRegistry.Build(state, _zoneIndex);
        var hand = _zoneIndex.GetInstanceIds(state, "ZoneType_Hand", mySeatId);
        var battlefield = _zoneIndex.GetInstanceIds(state, "ZoneType_Battlefield", mySeatId);
        var stack = _zoneIndex.GetInstanceIds(state, "ZoneType_Stack", mySeatId);

        var (myLife, opponentLife) = ReadLifeTotals(state, mySeatId);
        var mana = ReadManaPool(state, mySeatId);

        return new GameSnapshot(
            mySeatId,
            state.Turn,
            objects,
            hand,
            battlefield,
            stack,
            myLife,
            opponentLife,
            mana,
            state.PendingMessageCount);
    }

    private static (int MyLife, int OpponentLife) ReadLifeTotals(MutableGameState state, int mySeatId)
    {
        int? myLife = null;
        int? opponentLife = null;

        foreach (var player in state.Players)
        {
            if (!player.TryGetValue("systemSeatNumber", out var seatElement)
                || !seatElement.TryGetInt32(out var seat)
                || !player.TryGetValue("lifeTotal", out var lifeElement)
                || !lifeElement.TryGetInt32(out var life))
            {
                continue;
            }

            if (seat == mySeatId)
            {
                myLife = life;
            }
            else if (opponentLife is null)
            {
                opponentLife = life;
            }
        }

        return (myLife ?? 0, opponentLife ?? 0);
    }

    private static ManaPool ReadManaPool(MutableGameState state, int mySeatId)
    {
        foreach (var player in state.Players)
        {
            if (!player.TryGetValue("systemSeatNumber", out var seatElement)
                || !seatElement.TryGetInt32(out var seat)
                || seat != mySeatId
                || !player.TryGetValue("pipCount", out var pipElement)
                || !pipElement.TryGetInt32(out var pips))
            {
                continue;
            }

            return new ManaPool(0, 0, 0, 0, 0, pips);
        }

        return ManaPool.Empty;
    }
}
