using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MtgaBot.Actuate.Windows;

/// <summary>
/// Win32 mouse/keyboard via SetCursorPos + SendInput.
/// Verifies cursor moved; fails loudly when UIPI / elevation blocks input.
/// </summary>
public sealed class SendInputBackend : IInputBackend
{
    private static bool _dpiInitialized;

    public async Task ExecuteAsync(UiAction action, CancellationToken ct)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("SendInputBackend requires Windows.");
        }

        EnsureDpiAware();

        switch (action)
        {
            case MoveMouseAction move:
                MoveCursor(move.ScreenX, move.ScreenY);
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

    private static void MoveCursor(int screenX, int screenY)
    {
        if (!SetCursorPos(screenX, screenY))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SetCursorPos({screenX},{screenY}) failed.");
        }

        // Absolute SendInput as a second path (some environments ignore SetCursorPos alone).
        var (ax, ay) = ToAbsolute(screenX, screenY);
        var move = new INPUT
        {
            Type = INPUT_MOUSE,
            U = new InputUnion
            {
                Mi = new MOUSEINPUT
                {
                    Dx = ax,
                    Dy = ay,
                    DwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                },
            },
        };
        SendOrThrow([move]);

        if (!GetCursorPos(out var pos))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetCursorPos failed after move.");
        }

        // Allow a few pixels of DPI / virtual-desktop slop.
        if (Math.Abs(pos.X - screenX) > 8 || Math.Abs(pos.Y - screenY) > 8)
        {
            throw new InvalidOperationException(
                $"Mouse did not move (wanted {screenX},{screenY}, got {pos.X},{pos.Y}). " +
                "If MTGA runs as Administrator, run the CLI elevated too; check that nothing blocks synthetic input.");
        }
    }

    private static void MouseClick(MouseButton button)
    {
        var (down, up) = button == MouseButton.Right
            ? (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP)
            : (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);

        SendOrThrow(
        [
            new INPUT { Type = INPUT_MOUSE, U = new InputUnion { Mi = new MOUSEINPUT { DwFlags = down } } },
            new INPUT { Type = INPUT_MOUSE, U = new InputUnion { Mi = new MOUSEINPUT { DwFlags = up } } },
        ]);
    }

    private static void KeyTap(ushort virtualKey)
    {
        SendOrThrow(
        [
            new INPUT
            {
                Type = INPUT_KEYBOARD,
                U = new InputUnion { Ki = new KEYBDINPUT { WVk = virtualKey } },
            },
            new INPUT
            {
                Type = INPUT_KEYBOARD,
                U = new InputUnion { Ki = new KEYBDINPUT { WVk = virtualKey, DwFlags = KEYEVENTF_KEYUP } },
            },
        ]);
    }

    private static void SendOrThrow(INPUT[] inputs)
    {
        var size = Marshal.SizeOf<INPUT>();
        var sent = SendInput((uint)inputs.Length, inputs, size);
        if (sent != inputs.Length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"SendInput injected {sent}/{inputs.Length} (structSize={size}).");
        }
    }

    private static (int X, int Y) ToAbsolute(int screenX, int screenY)
    {
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vw <= 0 || vh <= 0)
        {
            vw = GetSystemMetrics(SM_CXSCREEN);
            vh = GetSystemMetrics(SM_CYSCREEN);
            vx = 0;
            vy = 0;
        }

        // Map pixel → 0..65535 over the virtual desktop.
        var ax = (int)Math.Round((screenX - vx) * 65535.0 / Math.Max(1, vw - 1));
        var ay = (int)Math.Round((screenY - vy) * 65535.0 / Math.Max(1, vh - 1));
        return (ax, ay);
    }

    private static void EnsureDpiAware()
    {
        if (_dpiInitialized)
        {
            return;
        }

        _dpiInitialized = true;
        try
        {
            // Best-effort; ignore failures on older Windows.
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
                // ignore
            }
        }
    }

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>x64-correct INPUT (type + padding + 32-byte union ≈ 40 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mi;
        [FieldOffset(0)] public KEYBDINPUT Ki;
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
