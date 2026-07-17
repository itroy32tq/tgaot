using System.Runtime.InteropServices;
using MtgaBot.Actuate;
using MtgaBot.Actuate.Windows;

namespace MtgaBot.Host.Actuate;

/// <summary>Moves the cursor and holds so the user can see it, then restores.</summary>
public static class MouseProbe
{
    public static async Task<int> RunAsync(TextWriter output)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            output.WriteLine("mouse-probe requires Windows.");
            return 1;
        }

        if (!GetCursorPos(out var before))
        {
            output.WriteLine("GetCursorPos failed.");
            return 1;
        }

        // Large, obvious jump (not +40,+20 which is easy to miss).
        var targetX = before.X + 200;
        var targetY = Math.Max(40, before.Y - 150);
        output.WriteLine($"before: ({before.X},{before.Y})");
        output.WriteLine($"moving to ({targetX},{targetY}) and holding 2 seconds — watch the cursor");
        output.WriteLine("(works on desktop / any normal window; some games hide the hardware cursor)");

        var backend = new SendInputBackend();
        try
        {
            await backend.ExecuteAsync(new MoveMouseAction(targetX, targetY), CancellationToken.None);
        }
        catch (Exception ex)
        {
            output.WriteLine($"FAIL: {ex.Message}");
            return 2;
        }

        if (!GetCursorPos(out var after))
        {
            output.WriteLine("GetCursorPos failed after move.");
            return 1;
        }

        output.WriteLine($"after:  ({after.X},{after.Y})");
        if (Math.Abs(after.X - targetX) > 8 || Math.Abs(after.Y - targetY) > 8)
        {
            output.WriteLine("FAIL: cursor did not reach target.");
            return 2;
        }

        await Task.Delay(2000);
        await backend.ExecuteAsync(new MoveMouseAction(before.X, before.Y), CancellationToken.None);
        output.WriteLine("restored. ok: mouse control works.");
        return 0;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
