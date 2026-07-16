namespace MtgaBot.Actuate;

public static class UiActionFormatter
{
    public static string Format(UiAction action) => action switch
    {
        MoveMouseAction m => $"Move({m.ScreenX},{m.ScreenY})",
        ClickAction c => $"Click({c.Button})",
        DoubleClickAction d => $"DoubleClick({d.Button})",
        KeyPressAction k => $"Key({k.Key})",
        DelayAction delay => $"Delay({delay.Duration.TotalMilliseconds:0}ms)",
        _ => action.GetType().Name,
    };

    public static string FormatAll(IEnumerable<UiAction> actions) =>
        string.Join(" → ", actions.Select(Format));
}
