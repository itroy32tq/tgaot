using System.Runtime.InteropServices;
using System.Text;

namespace MtgaBot.Actuate.Windows;

public sealed class Win32WindowLocator : IWindowLocator
{
    public WindowRect? FindMtgaClientRect()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        WindowRect? found = null;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var title = GetWindowTitle(hWnd);
            if (!IsMtgaTitle(title))
            {
                return true;
            }

            if (!TryGetClientScreenRect(hWnd, out var rect) || rect.Width < 960 || rect.Height < 540)
            {
                return true;
            }

            found = rect;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    public static bool IsMtgaTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var t = title.Trim();
        return t.Contains("MTGA", StringComparison.OrdinalIgnoreCase)
               || t.Contains("Magic: The Gathering Arena", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetClientScreenRect(IntPtr hWnd, out WindowRect rect)
    {
        rect = default;
        if (!GetClientRect(hWnd, out var client) || client.Right <= 0 || client.Bottom <= 0)
        {
            return false;
        }

        var topLeft = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hWnd, ref topLeft))
        {
            return false;
        }

        rect = new WindowRect(topLeft.X, topLeft.Y, client.Right - client.Left, client.Bottom - client.Top);
        return true;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        _ = GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
