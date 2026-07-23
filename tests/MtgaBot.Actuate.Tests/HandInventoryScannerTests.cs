namespace MtgaBot.Actuate.Tests;

public class LandPlayPickerTests
{
    [Fact]
    public void PickFirst_ReturnsLeftmostPlayable()
    {
        var inventory = new[]
        {
            new HandCardHit(10, 100, 1050, 100),
            new HandCardHit(20, 400, 1050, 400),
            new HandCardHit(30, 700, 1050, 700),
        };

        var pick = LandPlayPicker.PickFirst(inventory, [30, 20]);
        Assert.NotNull(pick);
        Assert.Equal(20, pick.InstanceId);
    }

    [Fact]
    public void PickFirst_NullWhenNoOverlap()
    {
        var inventory = new[] { new HandCardHit(10, 100, 1050, 100) };
        Assert.Null(LandPlayPicker.PickFirst(inventory, [99]));
    }
}

public class HandInventoryScannerTests
{
    [Fact]
    public async Task Scan_BuildsSingleHitFromConstantHover()
    {
        var profile = CalibrationProfile.CreateDefault() with { HandScanStep = 480 };
        var map = new CoordinateMap(new WindowRect(0, 0, 1920, 1080), profile);
        var input = new RecordingInputBackend();
        var scanner = new HandInventoryScanner(
            map,
            profile,
            input,
            new ImmediateHoverObjectIdSource(55),
            perPointTimeout: TimeSpan.FromMilliseconds(1),
            moveDelay: TimeSpan.Zero,
            resetDelay: TimeSpan.Zero);

        var hits = await scanner.ScanAsync(CancellationToken.None);

        Assert.Single(hits);
        Assert.Equal(55, hits[0].InstanceId);
        // Midpoint of full arc ≈ center.
        Assert.InRange(hits[0].DesignX, 800, 1120);
    }

    [Fact]
    public async Task Scan_DoesNotStopEarly_WalksFullArc()
    {
        var profile = CalibrationProfile.CreateDefault() with { HandScanStep = 480 };
        var map = new CoordinateMap(new WindowRect(0, 0, 1920, 1080), profile);
        var input = new RecordingInputBackend();
        var scanner = new HandInventoryScanner(
            map,
            profile,
            input,
            new ImmediateHoverObjectIdSource(1),
            perPointTimeout: TimeSpan.FromMilliseconds(1),
            moveDelay: TimeSpan.Zero,
            resetDelay: TimeSpan.Zero);

        await scanner.ScanAsync(CancellationToken.None);

        // reset + points for steps=1920/480=4 → i=0..4 = 5 arc points → 6 moves.
        var moves = input.Actions.OfType<MoveMouseAction>().Count();
        Assert.True(moves >= 6, $"expected full-arc moves, got {moves}");
        Assert.Contains(input.Actions.OfType<MoveMouseAction>(), m => m.ScreenX >= 1900);
    }
}
