using MtgaBot.Decide;

namespace MtgaBot.Actuate.Tests;

public class IntentExecutorTests
{
    private static IntentExecutor CreateExecutor(IHoverObjectIdSource? hover = null)
    {
        var profile = CalibrationProfile.CreateDefault();
        var map = new CoordinateMap(new WindowRect(0, 0, 1920, 1080), profile);
        return new IntentExecutor(
            map,
            profile,
            new RecordingInputBackend(),
            hover ?? new ImmediateHoverObjectIdSource(),
            hoverPointTimeout: TimeSpan.FromMilliseconds(1),
            hoverMoveDelay: TimeSpan.Zero);
    }

    [Fact]
    public async Task Pass_ClicksNextTwice()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(new PassPriorityIntent(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Actions, a => a is MoveMouseAction m && m.ScreenX == 1755 && m.ScreenY == 944);
        Assert.Equal(2, result.Actions.OfType<ClickAction>().Count());
    }

    [Fact]
    public async Task KeepHand_ClicksKeep()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(new KeepHandIntent(true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Actions, a => a is MoveMouseAction m && m.ScreenX == 1101 && m.ScreenY == 870);
        Assert.Single(result.Actions.OfType<ClickAction>());
    }

    [Fact]
    public async Task AttackAll_ClicksAttackButton()
    {
        var executor = CreateExecutor();
        var result = await executor.ExecuteAsync(new AttackAllIntent(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, result.Actions.OfType<ClickAction>().Count());
    }

    [Fact]
    public async Task Cast_HoverThenDoubleClick()
    {
        var executor = CreateExecutor(new ImmediateHoverObjectIdSource());
        var result = await executor.ExecuteAsync(new CastIntent(42), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Actions, a => a is MoveMouseAction);
        Assert.Contains(result.Actions, a => a is DoubleClickAction);
    }

    [Fact]
    public async Task Cast_HoverMiss_Fails()
    {
        var executor = CreateExecutor(new NeverHoverObjectIdSource());
        var result = await executor.ExecuteAsync(new CastIntent(42), CancellationToken.None);

        Assert.False(result.Success);
        Assert.DoesNotContain(result.Actions, a => a is DoubleClickAction);
    }

    [Fact]
    public async Task ActuatorBusy_SetDuringExecute()
    {
        var busy = new ActuatorBusy();
        var profile = CalibrationProfile.CreateDefault();
        var map = new CoordinateMap(new WindowRect(0, 0, 1920, 1080), profile);
        var gate = new SlowInputBackend();
        var executor = new IntentExecutor(map, profile, gate, new ImmediateHoverObjectIdSource(), busy);

        Assert.False(busy.IsBusy);
        var task = executor.ExecuteAsync(new PassPriorityIntent(), CancellationToken.None);
        await Task.Delay(20);
        Assert.True(busy.IsBusy);
        gate.Release();
        await task;
        Assert.False(busy.IsBusy);
    }

    [Fact]
    public void Win32WindowLocator_RecognizesTitles()
    {
        Assert.True(Windows.Win32WindowLocator.IsMtgaTitle("MTGA"));
        Assert.True(Windows.Win32WindowLocator.IsMtgaTitle("Magic: The Gathering Arena"));
        Assert.False(Windows.Win32WindowLocator.IsMtgaTitle("Notepad"));
    }

    private sealed class SlowInputBackend : IInputBackend
    {
        private readonly TaskCompletionSource _tcs = new();

        public void Release() => _tcs.TrySetResult();

        public async Task ExecuteAsync(UiAction action, CancellationToken ct)
        {
            if (action is MoveMouseAction)
            {
                await _tcs.Task.WaitAsync(ct);
            }
        }
    }
}
