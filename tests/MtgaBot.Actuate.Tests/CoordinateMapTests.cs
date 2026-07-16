namespace MtgaBot.Actuate.Tests;

public class CoordinateMapTests
{
    [Fact]
    public void ToScreen_IdentityOn1920()
    {
        var map = new CoordinateMap(new WindowRect(0, 0, 1920, 1080), CalibrationProfile.CreateDefault());
        var (x, y) = map.ToScreen(new DesignPoint(1755, 944));
        Assert.Equal(1755, x);
        Assert.Equal(944, y);
    }

    [Fact]
    public void ToScreen_ScalesHalfResolution()
    {
        var map = new CoordinateMap(new WindowRect(100, 50, 960, 540), CalibrationProfile.CreateDefault());
        var (x, y) = map.ToScreen(new DesignPoint(960, 540));
        Assert.Equal(100 + 480, x);
        Assert.Equal(50 + 270, y);
    }
}
