using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using GlassDesk.Core;

namespace GlassDesk.Platform;

public readonly record struct MaterialEnvironment(bool HighContrast, bool TransparencyEnabled, bool EnergySaver, bool AnimationsEnabled)
{
    public bool UseSolid => HighContrast || !TransparencyEnabled || EnergySaver;
    public bool ReduceMotion => !AnimationsEnabled || EnergySaver;
}

/// <summary>Owned HWND subclass, tray, optional hotkey and startup registration. Construct/dispose on the HWND UI thread.</summary>
public sealed class SystemIntegration : IDisposable
{
    private const uint TrayMessage = 0x8000 + 0x47;
    private const nuint SubclassId = 0x474c4453;
    private readonly nint _window;
    private readonly IUiDispatcher _dispatcher;
    private readonly Diagnostics _diagnostics;
    private readonly NativeMethods.SubclassProc _subclass;
    private readonly uint _taskbarCreated;
    private readonly InstanceIdentity _identity;
    private readonly uint _settingsMessage, _quitMessage;
    private readonly int _ownerThread;
    private int _hotkeyId;
    private uint _hotkeyModifiers;
    private uint _hotkeyKey;
    private bool _trayAdded;
    private readonly nint _trayIcon;
    private bool _disposed;
    public bool WidgetVisible { get; set; } = true;
    public event Action? ShowHideRequested;
    public event Action? SettingsRequested;
    public event Action? OpenMusicRequested;
    public event Action? ResetPositionRequested;
    public event Action? QuitRequested;
    public event Action? CollapseRequested;
    public event Action? DisplayChanged;
    public event Action? Resumed;
    public event Action? SystemSettingsChanged;

    public SystemIntegration(nint hostHandle, IUiDispatcher dispatcher, Diagnostics? diagnostics = null, InstanceIdentity? identity=null)
    {
        _identity=identity??InstanceIdentity.Formal;
        _settingsMessage=NativeMethods.RegisterWindowMessageW(_identity.SettingsMessageName);
        _quitMessage=NativeMethods.RegisterWindowMessageW(_identity.QuitMessageName);
        if (hostHandle == 0 || !NativeMethods.IsWindow(hostHandle)) throw new ArgumentException("A live widget HWND is required.", nameof(hostHandle));
        _window = hostHandle; _dispatcher = dispatcher; _diagnostics = diagnostics ?? new();
        _ownerThread = Environment.CurrentManagedThreadId;
        _subclass = WindowProcedure;
        _taskbarCreated = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        if (!NativeMethods.SetWindowSubclass(_window, _subclass, SubclassId, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!NativeMethods.SetPropW(_window, _identity.WindowMarker, 1))
        {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.RemoveWindowSubclass(_window, _subclass, SubclassId);
            throw new Win32Exception(error);
        }
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "GlassDesk.ico");
        // LR_LOADFROMFILE returns an owned icon. Keep it until the tray is removed.
        _trayIcon = NativeMethods.LoadImageW(0, iconPath, 1, 32, 32, 0x10);
        AddTray();
    }

    private void EnsureOwner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("System integration requires the HWND UI thread.");
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (!_disposed)
            {
                if (message == _taskbarCreated) { _trayAdded = false; AddTray(); }
                else if (message == _settingsMessage) { Dispatch(SettingsRequested); return 1; }
                else if (message == _quitMessage) { Dispatch(QuitRequested); return 1; }
                else if (message == 0x0312 && (int)wParam == _hotkeyId && _hotkeyId != 0) { Dispatch(ShowHideRequested); return 0; }
                else if (message == TrayMessage)
                {
                    var trayEvent = (uint)((long)lParam & 0xffff);
                    if (trayEvent is 0x0205 or 0x007b) ShowTrayMenu(); // RBUTTONUP/CONTEXTMENU
                    else if (trayEvent is 0x0400 or 0x0401 or 0x0203) Dispatch(SettingsRequested); // NIN_SELECT/KEYSELECT/DBLCLK
                    return 0;
                }
                else if (message == 0x007e) Dispatch(DisplayChanged);
                else if (message == 0x0218 && (uint)wParam is 7 or 18) Dispatch(Resumed);
                else if (message is 0x0015 or 0x001a or 0x001e or 0x031a or 0x0320 || (message == 0x0218 && (uint)wParam == 10)) Dispatch(SystemSettingsChanged);
                else if (message == 0x0082) Dispose(); // WM_NCDESTROY safety if native teardown preceded managed teardown.
            }
        }
        catch (Exception error) { _diagnostics.RecordError("system.message", error); }
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

    private void Dispatch(Action? action)
    {
        if (action is null) return;
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed) return;
            try { action(); } catch (Exception error) { _diagnostics.RecordError("system.callback", error); }
        });
    }

    private NativeMethods.NotifyIconData TrayData() => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(), Window = _window, Id = 1,
        Flags = 1 | 2 | 4 | 0x80, CallbackMessage = TrayMessage,
        Icon = _trayIcon != 0 ? _trayIcon : NativeMethods.LoadIconW(0, (nint)32512),
        Tip = _identity.IsPreview ? "GlassDesk UI Preview" : "GlassDesk · 桌面陪伴", Info = "", InfoTitle = "", Version = 4
    };

    private void AddTray()
    {
        if (_trayAdded || _disposed) return;
        var icon = TrayData();
        _trayAdded = NativeMethods.Shell_NotifyIconW(0, ref icon);
        if (_trayAdded) NativeMethods.Shell_NotifyIconW(4, ref icon);
        else _diagnostics.RecordWin32Error("tray.add", Marshal.GetLastWin32Error());
    }

    private void ShowTrayMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            NativeMethods.AppendMenuW(menu, 0, 1, WidgetVisible ? "隐藏组件" : "显示组件");
            NativeMethods.AppendMenuW(menu, 0, 2, "设置");
            NativeMethods.AppendMenuW(menu, 0, 3, "打开网易云音乐");
            NativeMethods.AppendMenuW(menu, 0, 4, "重置位置");
            NativeMethods.AppendMenuW(menu, 0, 6, "收起为胶囊");
            NativeMethods.AppendMenuW(menu, 0x800, 0, null);
            NativeMethods.AppendMenuW(menu, 0, 5, _identity.IsPreview?"退出 GlassDesk UI Preview":"退出 GlassDesk");
            NativeMethods.GetCursorPos(out var point);
            // Native menu takes its normal menu input; the no-activate widget is never explicitly foregrounded.
            var chosen = NativeMethods.TrackPopupMenuEx(menu, 0x0100 | 0x0002, point.X, point.Y, _window, 0);
            Dispatch(chosen switch { 1 => ShowHideRequested, 2 => SettingsRequested, 3 => OpenMusicRequested,
                4 => ResetPositionRequested, 5 => QuitRequested, 6 => CollapseRequested, _ => null });
            var icon = TrayData(); NativeMethods.Shell_NotifyIconW(3, ref icon);
            NativeMethods.PostMessageW(_window, 0, 0, 0);
        }
        finally { NativeMethods.DestroyMenu(menu); }
    }

    public bool TrySetHotkey(uint modifiers, uint key, out string? error)
    {
        EnsureOwner();
        error = null;
        if(_identity.IsPreview){error="预览版不修改系统启动和全局快捷键。";return false;}
        modifiers &= 0xf;
        if (key == _hotkeyKey && modifiers == _hotkeyModifiers) return true;
        if (key == 0)
        {
            if (_hotkeyId != 0) NativeMethods.UnregisterHotKey(_window, _hotkeyId);
            _hotkeyId = 0; _hotkeyKey = _hotkeyModifiers = 0;
            return true;
        }
        if (key > 255 || key is 16 or 17 or 18 or 91 or 92)
        { error = "请选择一个普通按键，可同时使用 Ctrl、Alt、Shift 或 Win。"; return false; }
        // Register the replacement under another ID BEFORE removing the old binding.
        var replacementId = _hotkeyId == 0x4701 ? 0x4702 : 0x4701;
        if (!NativeMethods.RegisterHotKey(_window, replacementId, modifiers | 0x4000, key))
        {
            _diagnostics.RecordWin32Error("hotkey.register", Marshal.GetLastWin32Error());
            error = "这个快捷键已被占用或由 Windows 保留，原绑定保持不变。";
            return false;
        }
        if (_hotkeyId != 0) NativeMethods.UnregisterHotKey(_window, _hotkeyId);
        _hotkeyId = replacementId; _hotkeyModifiers = modifiers; _hotkeyKey = key;
        return true;
    }

    /// <summary>Only call for a previously saved explicit preference, or after the user changes this setting.</summary>
    public bool SetStartupEnabled(bool enabled, out string? error)
    {
        EnsureOwner(); error = null;
        if(_identity.IsPreview){error="预览版不修改系统启动和全局快捷键。";return false;}
        try
        {
            var executable = Environment.ProcessPath;
            if (enabled && (string.IsNullOrEmpty(executable) || !executable.EndsWith("GlassDesk.exe", StringComparison.OrdinalIgnoreCase)))
            { error = "请从已安装的 GlassDesk 程序设置开机启动。"; return false; }
            using var key = enabled ? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true) :
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (enabled)
            {
                key!.SetValue("GlassDesk", $"\"{executable}\" --startup", RegistryValueKind.String);
            }
            else key?.DeleteValue("GlassDesk", false);
            return true;
        }
        catch (Exception failure) when (failure is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        { _diagnostics.RecordError("startup.update", failure); error = "无法更新当前用户的开机启动设置。"; return false; }
    }

    /// <summary>Explicit user command only; never starts the player from widget startup.</summary>
    public bool OpenNetEase(out string? error)
    {
        EnsureOwner(); error = null;
        try
        {
            // Let NetEase's own registered protocol restore/start its existing instance.
            Process.Start(new ProcessStartInfo("orpheus://") { UseShellExecute = true });
            return true;
        }
        catch (Exception first) when (first is Win32Exception or InvalidOperationException)
        {
            _diagnostics.RecordError("music.protocol", first);
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var appPath = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\cloudmusic.exe");
                    if (appPath?.GetValue(null) is not string path || !File.Exists(path.Trim('"'))) continue;
                    try { Process.Start(new ProcessStartInfo(path.Trim('"')) { UseShellExecute = true }); return true; }
                    catch (Exception fallback) when (fallback is Win32Exception or InvalidOperationException) { _diagnostics.RecordError("music.path", fallback); }
                }
            }
            error = "未找到网易云音乐，请先安装或手动打开客户端。"; return false;
        }
    }

    /// <summary>Opens Explorer at a folder, or selects a file inside its parent folder.</summary>
    public bool OpenExplorer(string path, bool selectFile, out string? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path)) { error = "路径为空。"; return false; }
            string target = selectFile ? $"/select,\"{path}\"" : $"\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
            return true;
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException or ArgumentException)
        {
            _diagnostics.RecordError("home.explorer", failure);
            error = "无法打开资源管理器。";
            return false;
        }
    }

    /// <summary>Launches one of the fixed, user-visible external shortcuts.</summary>
    public bool LaunchExternal(string target, out string? error)
    {
        error = null;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception failure) when (failure is Win32Exception or InvalidOperationException or ArgumentException)
        {
            _diagnostics.RecordError("home.shortcut", failure);
            error = "无法启动该应用。";
            return false;
        }
    }

    public static MaterialEnvironment ReadMaterialEnvironment()
    {
        var contrast = new NativeMethods.HighContrast { Size = (uint)Marshal.SizeOf<NativeMethods.HighContrast>() };
        NativeMethods.SystemParametersInfoW(0x0042, contrast.Size, ref contrast, 0);
        var transparency = true;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            if (key?.GetValue("EnableTransparency") is int value) transparency = value != 0;
        NativeMethods.GetSystemPowerStatus(out var power);
        var animations = !NativeMethods.SystemParametersInfoBool(0x1042, 0, out var enabled, 0) || enabled != 0;
        return new((contrast.Flags & 1) != 0, transparency, power.SystemStatusFlag == 1, animations);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Dispose system integration on the HWND UI thread.");
        _disposed = true;
        if (_hotkeyId != 0) NativeMethods.UnregisterHotKey(_window, _hotkeyId);
        if (_trayAdded) { var icon = TrayData(); NativeMethods.Shell_NotifyIconW(2, ref icon); _trayAdded = false; }
        if (_trayIcon != 0) NativeMethods.DestroyIcon(_trayIcon);
        NativeMethods.RemovePropW(_window, _identity.WindowMarker);
        NativeMethods.RemoveWindowSubclass(_window, _subclass, SubclassId);
        ShowHideRequested = SettingsRequested = OpenMusicRequested = ResetPositionRequested = QuitRequested = null;
        CollapseRequested=null;
        DisplayChanged = Resumed = SystemSettingsChanged = null;
    }
}
