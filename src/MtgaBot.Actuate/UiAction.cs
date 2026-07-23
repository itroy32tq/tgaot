namespace MtgaBot.Actuate;

public abstract record UiAction;

public sealed record MoveMouseAction(int ScreenX, int ScreenY) : UiAction;

public sealed record ClickAction(MouseButton Button = MouseButton.Left) : UiAction;

public sealed record DoubleClickAction(MouseButton Button = MouseButton.Left) : UiAction;

public sealed record MouseDownAction(MouseButton Button = MouseButton.Left) : UiAction;

public sealed record MouseUpAction(MouseButton Button = MouseButton.Left) : UiAction;

public sealed record KeyPressAction(string Key) : UiAction;

public sealed record DelayAction(TimeSpan Duration) : UiAction;

public enum MouseButton
{
    Left,
    Right,
}
