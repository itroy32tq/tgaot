namespace MtgaBot.Actuate;

/// <summary>
/// Full hand-arc scan: walk P1→P2 without early-exit and build an inventory of hovered cards.
/// Each unique objectId keeps the midpoint of its longest contiguous hover plateau.
/// </summary>
public sealed class HandInventoryScanner(
    ICoordinateMap map,
    CalibrationProfile profile,
    IInputBackend input,
    IHoverObjectIdSource hoverSource,
    TimeSpan? perPointTimeout = null,
    TimeSpan? moveDelay = null,
    TimeSpan? resetDelay = null,
    int? stepOverride = null)
{
    private readonly TimeSpan _perPointTimeout = perPointTimeout ?? TimeSpan.FromMilliseconds(40);
    private readonly TimeSpan _moveDelay = moveDelay ?? TimeSpan.FromMilliseconds(10);
    private readonly TimeSpan _resetDelay = resetDelay ?? TimeSpan.FromMilliseconds(300);
    private readonly int _step = Math.Max(1, stepOverride ?? profile.HandScanStep);

    public int Step => _step;

    public int PointCount
    {
        get
        {
            var dx = Math.Abs(profile.HandScanP2.X - profile.HandScanP1.X);
            return Math.Max(1, dx / _step) + 1;
        }
    }

    public TimeSpan EstimateDuration =>
        _resetDelay + TimeSpan.FromTicks((_moveDelay + _perPointTimeout).Ticks * PointCount);

    public async Task<IReadOnlyList<HandCardHit>> ScanAsync(CancellationToken ct)
    {
        hoverSource.Reset();
        var p1 = profile.HandScanP1;
        var p2 = profile.HandScanP2;
        var dx = p2.X - p1.X;
        var steps = Math.Max(1, Math.Abs(dx) / _step);

        var resetY = Math.Max(0, p1.Y - 100);
        var (rx, ry) = map.ToScreen(new DesignPoint(p1.X, resetY));
        await input.ExecuteAsync(new MoveMouseAction(rx, ry), ct).ConfigureAwait(false);
        await input.ExecuteAsync(new DelayAction(_resetDelay), ct).ConfigureAwait(false);
        hoverSource.Reset();

        // Longest plateau per instanceId: (startDesignX, endDesignX, screenY, length).
        var best = new Dictionary<int, (int StartX, int EndX, int ScreenY, int Length)>();
        int? segmentId = null;
        var segmentStartX = 0;
        var segmentEndX = 0;
        var segmentScreenY = 0;
        var segmentLen = 0;

        void FlushSegment()
        {
            if (segmentId is not { } id || segmentLen <= 0)
            {
                return;
            }

            if (!best.TryGetValue(id, out var prev) || segmentLen > prev.Length)
            {
                best[id] = (segmentStartX, segmentEndX, segmentScreenY, segmentLen);
            }

            segmentId = null;
            segmentLen = 0;
        }

        for (var i = 0; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();
            var designX = p1.X + (int)Math.Round(dx * (i / (double)steps));
            var design = new DesignPoint(designX, p1.Y);
            var (sx, sy) = map.ToScreen(design);
            await input.ExecuteAsync(new MoveMouseAction(sx, sy), ct).ConfigureAwait(false);
            await input.ExecuteAsync(new DelayAction(_moveDelay), ct).ConfigureAwait(false);

            var observed = await hoverSource.WaitForAnyAsync(_perPointTimeout, ct).ConfigureAwait(false);
            if (observed is not { } id)
            {
                FlushSegment();
                continue;
            }

            if (segmentId != id)
            {
                FlushSegment();
                segmentId = id;
                segmentStartX = designX;
                segmentEndX = designX;
                segmentScreenY = sy;
                segmentLen = 1;
            }
            else
            {
                segmentEndX = designX;
                segmentScreenY = sy;
                segmentLen++;
            }
        }

        FlushSegment();

        return best
            .Select(kv =>
            {
                var midDesignX = (kv.Value.StartX + kv.Value.EndX) / 2;
                var (sx, _) = map.ToScreen(new DesignPoint(midDesignX, p1.Y));
                return new HandCardHit(kv.Key, sx, kv.Value.ScreenY, midDesignX);
            })
            .OrderBy(h => h.DesignX)
            .ToList();
    }
}
