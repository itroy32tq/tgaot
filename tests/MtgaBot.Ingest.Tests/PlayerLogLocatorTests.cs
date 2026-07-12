using MtgaBot.Ingest;

namespace MtgaBot.Ingest.Tests;

public class PlayerLogLocatorTests
{
    [Fact]
    public void GetDefaultPlayerLogPath_OnWindows_ContainsMtgaFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var locator = new PlayerLogLocator();
        var path = locator.GetDefaultPlayerLogPath();

        Assert.Contains("Wizards Of The Coast", path);
        Assert.EndsWith("Player.log", path);
    }
}
