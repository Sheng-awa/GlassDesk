using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace GlassDesk.App;

/// <summary>Two real window regions leave desktop-visible space between the pane and floating navigation.</summary>
internal sealed class SettingsWindowShape : IDisposable
{
    readonly nint _window;
    ShapeKey? _last;
    bool _disposed;

    public SettingsWindowShape(nint window) => _window = window;

    public static void ConfigureFrame(nint window)
    {
        nint style = GetWindowLongPtr(window, -16);
        SetWindowLongPtr(window, -16, new nint((style.ToInt64() & ~0x00CF0000L) | 0x80000000L));
        uint border = 0xFFFFFFFE;
        DwmSetWindowAttribute(window, 34, ref border, sizeof(uint));
        SetWindowPos(window, 0, 0, 0, 0, 0, 0x0037); // FrameChanged, no move/size/z-order/activation.
    }

    public bool Update(FrameworkElement root, FrameworkElement pane, FrameworkElement navigation)
    {
        if (_disposed || root.ActualWidth <= 0 || root.ActualHeight <= 0 || pane.ActualHeight <= 0 || navigation.ActualWidth <= 0) return true;
        double scale = root.XamlRoot?.RasterizationScale ?? Math.Max(96u, GetDpiForWindow(_window)) / 96d;
        var panePosition = pane.TransformToVisual(root).TransformPoint(new Point());
        var navPosition = navigation.TransformToVisual(root).TransformPoint(new Point());
        static int Pixel(double value, double scale) => (int)Math.Round(value * scale);
        var key = new ShapeKey(Pixel(panePosition.X, scale), Pixel(panePosition.Y, scale),
            Pixel(panePosition.X + pane.ActualWidth, scale), Pixel(panePosition.Y + pane.ActualHeight, scale), Pixel(UiRadius.Shell * 2, scale),
            Pixel(navPosition.X, scale), Pixel(navPosition.Y, scale),
            Pixel(navPosition.X + navigation.ActualWidth, scale), Pixel(navPosition.Y + navigation.ActualHeight, scale), Pixel(UiRadius.Shell * 2, scale));
        if (_last == key) return true;
        nint paneRegion = CreateRoundRectRgn(key.PaneLeft, key.PaneTop, key.PaneRight + 1, key.PaneBottom + 1, key.PaneDiameter, key.PaneDiameter);
        nint navRegion = CreateRoundRectRgn(key.NavLeft, key.NavTop, key.NavRight + 1, key.NavBottom + 1, key.NavDiameter, key.NavDiameter);
        try
        {
            if (paneRegion == 0 || navRegion == 0 || CombineRgn(paneRegion, paneRegion, navRegion, 2) == 0) return false;
            if (SetWindowRgn(_window, paneRegion, true) == 0) return false;
            // Win32 owns the transferred region and releases it on replacement/close.
            paneRegion = 0; _last = key; return true;
        }
        finally
        {
            if (paneRegion != 0) DeleteObject(paneRegion);
            if (navRegion != 0) DeleteObject(navRegion);
        }
    }

    public static void BeginDrag(nint window)
    {
        ReleaseCapture();
        SendMessage(window, 0x00A1, 2, 0); // WM_NCLBUTTONDOWN, HTCAPTION.
    }

    public void Dispose() { _disposed = true; _last = null; }

    readonly record struct ShapeKey(int PaneLeft, int PaneTop, int PaneRight, int PaneBottom, int PaneDiameter,
        int NavLeft, int NavTop, int NavRight, int NavBottom, int NavDiameter);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint window, uint attribute, ref uint value, int size);
    [DllImport("gdi32.dll")] static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
    [DllImport("gdi32.dll")] static extern int CombineRgn(nint destination, nint source1, nint source2, int mode);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")] static extern int SetWindowRgn(nint window, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern nint SendMessage(nint window, uint message, nint wparam, nint lparam);
}
