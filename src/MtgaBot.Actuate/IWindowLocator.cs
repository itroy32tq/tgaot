namespace MtgaBot.Actuate;

public interface IWindowLocator
{
    /// <summary>Find MTGA client rect in screen coordinates, or null if not found.</summary>
    WindowRect? FindMtgaClientRect();
}
