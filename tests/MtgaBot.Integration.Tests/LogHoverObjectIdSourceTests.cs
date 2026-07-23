using MtgaBot.Host.Actuate;

namespace MtgaBot.Integration.Tests;

public class LogHoverObjectIdSourceTests
{
    [Fact]
    public async Task WaitFor_MatchesObservedId()
    {
        var source = new LogHoverObjectIdSource();
        source.Reset();

        var wait = source.WaitForAsync(160, TimeSpan.FromSeconds(2), CancellationToken.None);
        await Task.Delay(20);
        source.ObserveLine("""{"greToClientEvent":{"greToClientMessages":[{"uiMessage":{"hover":{"objectId":160}}}]}}""");

        Assert.True(await wait);
    }

    [Fact]
    public async Task WaitFor_TimeoutOnMiss()
    {
        var source = new LogHoverObjectIdSource();
        source.Reset();

        var ok = await source.WaitForAsync(160, TimeSpan.FromMilliseconds(40), CancellationToken.None);
        Assert.False(ok);
    }

    [Fact]
    public async Task Reset_ClearsPending()
    {
        var source = new LogHoverObjectIdSource();
        source.ObserveLine("""{"greToClientEvent":{"greToClientMessages":[{"uiMessage":{"hover":{"objectId":7}}}]}}""");
        source.Reset();

        var ok = await source.WaitForAsync(7, TimeSpan.FromMilliseconds(30), CancellationToken.None);
        Assert.False(ok);
    }

    [Fact]
    public async Task WaitForAny_ReturnsObservedId()
    {
        var source = new LogHoverObjectIdSource();
        source.Reset();

        var wait = source.WaitForAnyAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        await Task.Delay(20);
        source.ObserveLine("""{"greToClientEvent":{"greToClientMessages":[{"uiMessage":{"hover":{"objectId":160}}}]}}""");

        Assert.Equal(160, await wait);
    }

    [Fact]
    public async Task WaitForAny_TimeoutOnMiss()
    {
        var source = new LogHoverObjectIdSource();
        source.Reset();

        var id = await source.WaitForAnyAsync(TimeSpan.FromMilliseconds(40), CancellationToken.None);
        Assert.Null(id);
    }
}
