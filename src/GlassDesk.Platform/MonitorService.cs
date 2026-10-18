using System.Runtime.InteropServices;
using GlassDesk.Core;

namespace GlassDesk.Platform;

public sealed class MonitorService
{
    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        var monitors = new List<MonitorDescriptor>();
        NativeMethods.EnumMonitorProc callback = (nint handle, nint dc, ref NativeMethods.Rect bounds, nint data) =>
        {
            var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>(), Device = "" };
            if (!NativeMethods.GetMonitorInfoW(handle, ref info)) return true;
            var device = new NativeMethods.DisplayDevice { Size = (uint)Marshal.SizeOf<NativeMethods.DisplayDevice>(),
                DeviceName = "", DeviceString = "", DeviceId = "", DeviceKey = "" };
            // EDD_GET_DEVICE_INTERFACE_NAME supplies the monitor device-interface identity, not the mutable DISPLAYn label.
            var stableId = NativeMethods.EnumDisplayDevicesW(info.Device, 0, ref device, 1) && !string.IsNullOrEmpty(device.DeviceId)
                ? device.DeviceId : info.Device;
            monitors.Add(new(stableId, ToRect(info.Bounds), ToRect(info.WorkArea), ReadScale(handle, info.Bounds), (info.Flags & 1) != 0));
            return true;
        };
        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return monitors;
    }

    private static float ReadScale(nint monitor, NativeMethods.Rect bounds)
    {
        // GetDpiForMonitor is not recommended on a per-monitor-aware thread. A short-lived hidden
        // PMv2 window at the target monitor obtains the documented GetDpiForWindow value instead.
        // This changes only this thread's temporary awareness, never Windows display settings.
        var previousAwareness = NativeMethods.SetThreadDpiAwarenessContext(-4);
        nint probe = 0;
        try
        {
            probe = NativeMethods.CreateWindowExW(0x08000000 | 0x00000080, "STATIC", "", 0x80000000,
                bounds.Left + (bounds.Right - bounds.Left) / 2, bounds.Top + (bounds.Bottom - bounds.Top) / 2,
                1, 1, 0, 0, NativeMethods.GetModuleHandleW(null), 0);
            var dpi = probe == 0 ? 0 : NativeMethods.GetDpiForWindow(probe);
            if (dpi > 0) return dpi / 96f;
            return NativeMethods.GetScaleFactorForMonitor(monitor, out var scale) >= 0 && scale > 0 ? scale / 100f : 1;
        }
        finally
        {
            if (probe != 0) NativeMethods.DestroyWindow(probe);
            if (previousAwareness != 0) NativeMethods.SetThreadDpiAwarenessContext(previousAwareness);
        }
    }

    internal static ScreenRect ToRect(NativeMethods.Rect rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);

    public static MonitorDescriptor? SelectMonitor(IReadOnlyList<MonitorDescriptor> monitors, string? stableId) =>
        monitors.FirstOrDefault(m => string.Equals(m.Id, stableId, StringComparison.OrdinalIgnoreCase)) ??
        monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();

    public static (int X, int Y) RestoreAnchor(MonitorDescriptor monitor, WidgetPreferences preferences, float widthDip = 200, float heightDip = 52)
    {
        var work = monitor.WorkArea;
        var width = Math.Max(1, (int)Math.Round(widthDip * monitor.Scale));
        var height = Math.Max(1, (int)Math.Round(heightDip * monitor.Scale));
        var x = work.X + (int)Math.Round(work.Width * preferences.AnchorX - width / 2d);
        var y = work.Y + (int)Math.Round(preferences.AnchorY * monitor.Scale);
        return (Math.Clamp(x, work.X, Math.Max(work.X, work.Right - width)),
            Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - height)));
    }
}
