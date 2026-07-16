namespace MtgaBot.Actuate;

/// <summary>Test / dry-run locator with a fixed client rect (default 1920×1080 at 0,0).</summary>
public sealed class FixedWindowLocator(WindowRect rect) : IWindowLocator
{
    public static FixedWindowLocator DesignDefault { get; } =
        new(new WindowRect(0, 0, 1920, 1080));

    public WindowRect? FindMtgaClientRect() => rect;
}
