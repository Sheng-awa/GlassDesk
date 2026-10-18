using System.Runtime.InteropServices;
using System.Text;
using GlassDesk.Core;

namespace GlassDesk.Platform;

/// <summary>WinEvent-driven external-window observer; no timer or geometry polling while idle.</summary>
public sealed class ForegroundObserver : IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly Diagnostics _diagnostics;
    private readonly NativeMethods.WinEventProc _callback;
    private readonly List<nint> _hooks = new();
    private bool _queued;
    private bool _disposed;
    private SystemScene _current;
    public SystemScene Current => _current;
    public event Action<SystemScene>? SceneChanged;

    public ForegroundObserver(IUiDispatcher dispatcher, Diagnostics? diagnostics = null)
    {
        _dispatcher = dispatcher; _diagnostics = diagnostics ?? new(); _callback = OnWinEvent;
    }

    // Create, Start and Dispose on the UI thread, which receives out-of-context WinEvent callbacks.
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hooks.Count != 0) return;
        AddHook(0x0003, 0x0003); // EVENT_SYSTEM_FOREGROUND
        AddHook(0x0010, 0x0011); // MINIMIZESTART/END
        AddHook(0x800b, 0x800b); // EVENT_OBJECT_LOCATIONCHANGE
        AddHook(0x8001, 0x8003); // DESTROY/SHOW/HIDE
        Refresh();
    }

    private void AddHook(uint minimum, uint maximum)
    {
        var hook = NativeMethods.SetWinEventHook(minimum, maximum, 0, _callback, 0, 0, 0);
        if (hook != 0) _hooks.Add(hook);
        else _diagnostics.RecordWin32Error("foreground.hook", Marshal.GetLastWin32Error());
    }

    private void OnWinEvent(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (_disposed || objectId != 0 || childId != 0) return;
        if (eventId != 3 && window != _current.ForegroundWindow && window != NativeMethods.GetForegroundWindow()) return;
        if (_queued) return;
        _queued = true;
        if (!_dispatcher.TryEnqueue(() => { _queued = false; if (!_disposed) Refresh(); })) _queued = false;
    }

    public void Refresh()
    {
        if (_disposed) return;
        try
        {
            var window = NativeMethods.GetForegroundWindow();
            if (window != 0) window = NativeMethods.GetAncestor(window, 2); // GA_ROOT
            // Opening GlassDesk settings must not turn an existing external full-screen scene into a different scene.
            if (IsOwnWindow(window))
            {
                window = _current.ForegroundWindow;
                if (window != 0 && !NativeMethods.IsWindow(window)) window = 0;
            }
            var scene = new SystemScene(window, false, false);
            if (window != 0 && !IsShell(window) && NativeMethods.IsWindowVisible(window) && !NativeMethods.IsIconic(window))
            {
                if (NativeMethods.DwmGetWindowIntAttribute(window, 14, out var cloaked, 4) >= 0 && cloaked != 0)
                {
                    scene = new(0, false, false);
                    if (scene != _current) { _current = scene; SceneChanged?.Invoke(scene); }
                    return;
                }
                if (NativeMethods.DwmGetWindowAttribute(window, 9, out var rectangle, (uint)Marshal.SizeOf<NativeMethods.Rect>()) < 0)
                    NativeMethods.GetWindowRect(window, out rectangle);
                var monitor = NativeMethods.MonitorFromWindow(window, 2);
                var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>(), Device = "" };
                if (monitor != 0 && NativeMethods.GetMonitorInfoW(monitor, ref info))
                {
                    var bounds = MonitorService.ToRect(rectangle);
                    var screen = MonitorService.ToRect(info.Bounds);
                    var work = MonitorService.ToRect(info.WorkArea);
                    var maximized = NativeMethods.IsZoomed(window);
                    var style = (long)NativeMethods.GetWindowLongPtrW(window, -16);
                    scene = new(window, IsFullscreen(bounds, screen, work, maximized, (style & 0x00c00000) != 0), maximized);
                }
            }
            if (scene != _current) { _current = scene; SceneChanged?.Invoke(scene); }
        }
        catch (Exception error) { _diagnostics.RecordError("foreground.refresh", error); }
    }

    public static bool Covers(ScreenRect window, ScreenRect target, int tolerance = 2) =>
        window.Width > 0 && window.Height > 0 && window.X <= target.X + tolerance && window.Y <= target.Y + tolerance &&
        window.Right >= target.Right - tolerance && window.Bottom >= target.Bottom - tolerance;

    public static bool IsFullscreen(ScreenRect window, ScreenRect monitor, ScreenRect workArea, bool maximized, bool hasCaption)
    {
        // A normal maximized captioned app remains ordinary even when an auto-hidden taskbar makes workarea==monitor.
        if (maximized && hasCaption) return false;
        var workAreaOnly = maximized && Covers(window, workArea) && !Covers(window, monitor);
        return Covers(window, monitor) && !workAreaOnly;
    }

    private static bool IsOwnWindow(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var process);
        return process == Environment.ProcessId;
    }

    private static bool IsShell(nint window)
    {
        if (window == NativeMethods.GetDesktopWindow() || window == NativeMethods.GetShellWindow()) return true;
        var name = new StringBuilder(256);
        NativeMethods.GetClassNameW(window, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        foreach (var hook in _hooks) NativeMethods.UnhookWinEvent(hook);
        _hooks.Clear(); SceneChanged = null;
    }
}
