namespace MtgaBot.Actuate;

/// <summary>One card observed during a full hand-arc inventory scan.</summary>
public sealed record HandCardHit(int InstanceId, int ScreenX, int ScreenY, int DesignX);
