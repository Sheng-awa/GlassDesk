using System.Runtime.InteropServices;
using GlassDesk.Core;

namespace GlassDesk.Platform;

/// <summary>Four event-driven background points; no image allocation or capture loop.</summary>
public static class WidgetTextToneProbe
{
    public static WidgetReadabilityDecision ReadPolicy(HostContext card, float textY, float tintOpacity,
        bool clockWhiteplate, bool previousLight, double primaryMinimumContrast = 4.5, bool hasRequiredSecondary = true)
    {
        if (card.Dpi == 0 || card.Width <= 0 || card.Height <= 0)
            return WidgetReadabilityPolicy.Resolve([], tintOpacity, clockWhiteplate, previousLight, primaryMinimumContrast, hasRequiredSecondary);
        nint dc = GetDC(0);
        if (dc == 0) return WidgetReadabilityPolicy.Resolve([], tintOpacity, clockWhiteplate, previousLight, primaryMinimumContrast, hasRequiredSecondary);
        try
        {
            float scale = card.Dpi / 96f;
            int left = card.X - (int)Math.Ceiling(28 * scale);
            int right = card.X + (int)Math.Ceiling((card.Width + 28) * scale);
            int y1 = card.Y + (int)Math.Round(Math.Clamp(textY, 8, card.Height - 8) * scale);
            int y2 = card.Y + (int)Math.Round(Math.Clamp(textY + 20, 8, card.Height - 8) * scale);
            uint[] colors = [GetPixel(dc, left, y1), GetPixel(dc, right, y1), GetPixel(dc, left, y2), GetPixel(dc, right, y2)];
            return WidgetReadabilityPolicy.Resolve(colors, tintOpacity, clockWhiteplate, previousLight, primaryMinimumContrast, hasRequiredSecondary);
        }
        finally { ReleaseDC(0, dc); }
    }

    public static bool TryRead(HostContext card, float textY, bool previousLight, out bool light)
    {
        light = previousLight;
        if (card.Dpi == 0 || card.Width <= 0 || card.Height <= 0) return false;
        nint dc = GetDC(0);
        if (dc == 0) return false;
        try
        {
            // Stay outside the excluded widget/shadow capture surfaces. Reading
            // their protected pixels would incorrectly classify light glass black.
            float scale = card.Dpi / 96f;
            int left = card.X - (int)Math.Ceiling(28 * scale);
            int right = card.X + (int)Math.Ceiling((card.Width + 28) * scale);
            int y1 = card.Y + (int)Math.Round(Math.Clamp(textY, 8, card.Height - 8) * scale);
            int y2 = card.Y + (int)Math.Round(Math.Clamp(textY + 20, 8, card.Height - 8) * scale);
            uint[] colors = [GetPixel(dc, left, y1), GetPixel(dc, right, y1), GetPixel(dc, left, y2), GetPixel(dc, right, y2)];
            return TryChooseLight(colors, previousLight, out light);
        }
        finally { ReleaseDC(0, dc); }
    }

    public static bool TryChooseLight(ReadOnlySpan<uint> colors, bool previousLight, out bool light)
    {
        light = previousLight;
        Span<double> luminance = stackalloc double[4];
        int count = 0;
        foreach (uint color in colors)
        {
            if (color == uint.MaxValue) continue;
            // COLORREF is 0x00BBGGRR, unlike the renderer's ARGB colors.
            double value = .2126 * Linear(color & 255) + .7152 * Linear((color >> 8) & 255) + .0722 * Linear((color >> 16) & 255);
            if (count < luminance.Length) luminance[count++] = value;
        }
        if (count == 0) return false;
        luminance[..count].Sort();
        double median = (luminance[(count - 1) / 2] + luminance[count / 2]) / 2;
        // Hysteresis keeps near-threshold backgrounds from flipping the ink.
        light = median < (previousLight ? .225 : .175);
        return true;
    }

    static double Linear(uint channel)
    {
        double value = channel / 255d;
        return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
    }
    [DllImport("user32.dll")] static extern nint GetDC(nint window);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(nint dc, int x, int y);
}
