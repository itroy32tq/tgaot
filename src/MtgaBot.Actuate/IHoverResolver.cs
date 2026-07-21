namespace MtgaBot.Actuate;

public enum HandClickProfile
{
    DoubleClick = 0,
    SingleClick = 1,
}

public interface IHoverResolver
{
    /// <summary>
    /// Scan hand arc, wait for matching objectId, then click per <paramref name="clickProfile"/>.
    /// Returns false on miss / timeout.
    /// </summary>
    Task<bool> ClickHandCardAsync(
        int instanceId,
        CancellationToken ct,
        HandClickProfile clickProfile = HandClickProfile.DoubleClick);
}

public sealed class HoverResolver(
    ICoordinateMap map,
    CalibrationProfile profile,
    IInputBackend input,
    IHoverObjectIdSource hoverSource,
    TimeSpan? perPointTimeout = null,
    TimeSpan? moveDelay = null,
    TimeSpan? resetDelay = null) : IHoverResolver
{
    private readonly TimeSpan _perPointTimeout = perPointTimeout ?? TimeSpan.FromMilliseconds(40);
    private readonly TimeSpan _moveDelay = moveDelay ?? TimeSpan.FromMilliseconds(10);
    private readonly TimeSpan _resetDelay = resetDelay ?? TimeSpan.FromMilliseconds(300);

    public async Task<bool> ClickHandCardAsync(
        int instanceId,
        CancellationToken ct,
        HandClickProfile clickProfile = HandClickProfile.DoubleClick)
    {
        hoverSource.Reset();
        var step = Math.Max(1, profile.HandScanStep);
        var p1 = profile.HandScanP1;
        var p2 = profile.HandScanP2;
        var dx = p2.X - p1.X;
        var steps = Math.Max(1, Math.Abs(dx) / step);

        // Clear sticky hover before scanning the hand arc (Lotus RESET_BEFORE_HAND_SELECT).
        var resetY = Math.Max(0, p1.Y - 100);
        var (rx, ry) = map.ToScreen(new DesignPoint(p1.X, resetY));
        await input.ExecuteAsync(new MoveMouseAction(rx, ry), ct).ConfigureAwait(false);
        await input.ExecuteAsync(new DelayAction(_resetDelay), ct).ConfigureAwait(false);
        hoverSource.Reset();

        for (var i = 0; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();
            var designX = p1.X + (int)Math.Round(dx * (i / (double)steps));
            var design = new DesignPoint(designX, p1.Y);
            var (sx, sy) = map.ToScreen(design);
            await input.ExecuteAsync(new MoveMouseAction(sx, sy), ct).ConfigureAwait(false);
            await input.ExecuteAsync(new DelayAction(_moveDelay), ct).ConfigureAwait(false);

            if (!await hoverSource.WaitForAsync(instanceId, _perPointTimeout, ct).ConfigureAwait(false)) continue;

            if (clickProfile == HandClickProfile.SingleClick)
            {
                await input.ExecuteAsync(new ClickAction(), ct).ConfigureAwait(false);
            }
            else
            {
                await input.ExecuteAsync(new DoubleClickAction(), ct).ConfigureAwait(false);
            }

            return true;
        }

        return false;
    }
}
