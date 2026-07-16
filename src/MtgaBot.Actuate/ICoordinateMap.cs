namespace MtgaBot.Actuate;

public interface ICoordinateMap
{
    WindowRect ClientRect { get; }

    /// <summary>Map design-space point to absolute screen coordinates.</summary>
    (int X, int Y) ToScreen(DesignPoint design);
}

public sealed class CoordinateMap(WindowRect clientRect, CalibrationProfile profile) : ICoordinateMap
{
    public WindowRect ClientRect { get; } = clientRect;

    public (int X, int Y) ToScreen(DesignPoint design)
    {
        if (profile.DesignWidth <= 0 || profile.DesignHeight <= 0)
        {
            throw new InvalidOperationException("Calibration design size must be positive.");
        }

        var x = ClientRect.Left + (int)Math.Round(design.X / (double)profile.DesignWidth * ClientRect.Width);
        var y = ClientRect.Top + (int)Math.Round(design.Y / (double)profile.DesignHeight * ClientRect.Height);
        return (x, y);
    }
}
