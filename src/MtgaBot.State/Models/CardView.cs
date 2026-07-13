namespace MtgaBot.State;

public sealed record CardView(
    int InstanceId,
    int GrpId,
    int ZoneId,
    string ZoneType,
    int OwnerSeatId,
    bool Tapped);
