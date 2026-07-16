using System.Runtime.InteropServices;

namespace MtgaBot.Actuate.Windows;

public sealed class SendInputBackend : IInputBackend
{
    public async Task ExecuteAsync(UiAction action, CancellationToken ct)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("SendInputBackend requires Windows.");
        }

        switch (action)
        {
            case MoveMouseAction move:
                SetCursorPos(move.ScreenX, move.ScreenY);
                break;
            case ClickAction click:
                MouseClick(click.Button);
                break;
            case DoubleClickAction dbl:
                MouseClick(dbl.Button);
                await Task.Delay(80, ct).ConfigureAwait(false);
                MouseClick(dbl.Button);
                break;
            case KeyPressAction key when string.Equals(key.Key, "Enter", StringComparison.OrdinalIgnoreCase):
                KeyTap(0x0D); // VK_RETURN
                break;
            case DelayAction delay:
                await Task.Delay(delay.Duration, ct).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"Unsupported UiAction: {action.GetType().Name}");
        }
    }

    private static void MouseClick(MouseButton button)
    {
        var (down, up) = button == MouseButton.Right
            ? (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP)
            : (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);

        var inputs = new INPUT[2];
        inputs[0].Type = INPUT_MOUSE;
        inputs[0].Data.Mouse.DwFlags = down;
        inputs[1].Type = INPUT_MOUSE;
        inputs[1].Data.Mouse.DwFlags = up;
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void KeyTap(ushort virtualKey)
    {
        var inputs = new INPUT[2];
        inputs[0].Type = INPUT_KEYBOARD;
        inputs[0].Data.Keyboard.WVk = virtualKey;
        inputs[1].Type = INPUT_KEYBOARD;
        inputs[1].Data.Keyboard.WVk = virtualKey;
        inputs[1].Data.Keyboard.DwFlags = KEYEVENTF_KEYUP;
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort WVk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }
}
