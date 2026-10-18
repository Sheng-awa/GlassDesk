using System.Runtime.InteropServices;
using GlassDesk.Core;

namespace GlassDesk.Platform;

/// <summary>Reads the cursor on the current input desktop without hooks or mouse capture.</summary>
public static class CursorPositionSource
{
    public static bool TryGetRelativePosition(HostContext card, out float x, out float y)
    {
        x = y = 0;
        if (card.Dpi == 0 || !GetCursorPos(out var point)) return false;
        float scale = card.Dpi / 96f;
        x = (point.X - card.X) / scale;
        y = (point.Y - card.Y) / scale;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X, Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out Point point);
}
