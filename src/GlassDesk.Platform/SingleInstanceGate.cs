using System.Diagnostics;
using GlassDesk.Core;

namespace GlassDesk.Platform;

public sealed class SingleInstanceGate : IDisposable
{
    internal const string WindowMarker = "GlassDesk.Resident.v1.643A4E18";
    private readonly Mutex _mutex;
    private bool _ownsMutex;
    private readonly InstanceIdentity _identity;
    private readonly uint _settingsMessage, _quitMessage;
    public bool IsPrimary => _ownsMutex;
    internal static uint SettingsMessage { get; } = NativeMethods.RegisterWindowMessageW("GlassDesk.OpenSettings.v1.643A4E18");
    internal static uint QuitMessage { get; } = NativeMethods.RegisterWindowMessageW("GlassDesk.Quit.v1.643A4E18");

    public SingleInstanceGate() : this(InstanceIdentity.Formal) { }
    public SingleInstanceGate(InstanceIdentity identity)
    {
        _identity=identity;
        _settingsMessage=NativeMethods.RegisterWindowMessageW(identity.SettingsMessageName);
        _quitMessage=NativeMethods.RegisterWindowMessageW(identity.QuitMessageName);
        _mutex = new Mutex(false, identity.MutexName);
        try { _ownsMutex = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
    }

    public bool RequestExistingSettings() => SignalWithRetry(_settingsMessage);
    public bool RequestQuit() => SignalWithRetry(_quitMessage);
    private bool SignalWithRetry(uint message)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            var remaining = 2000 - (int)clock.ElapsedMilliseconds;
            if (remaining <= 0) break;
            if (Signal(message, clock)) return true;
            remaining = 2000 - (int)clock.ElapsedMilliseconds;
            if (remaining <= 0) break;
            Thread.Sleep(Math.Min(100, remaining));
        }
        while (clock.ElapsedMilliseconds < 2000);
        return false;
    }

    private bool Signal(uint message, Stopwatch clock)
    {
        var sent = false;
        NativeMethods.EnumWindowsProc callback = (window, data) =>
        {
            var remaining = 2000 - (int)clock.ElapsedMilliseconds;
            if (remaining <= 0) return false;
            if (NativeMethods.GetPropW(window, _identity.WindowMarker) == 0) return true;
            if (NativeMethods.SendMessageTimeoutW(window, message, 0, 0, 2, (uint)Math.Min(1000, remaining), out _) != 0) sent = true;
            return !sent;
        };
        NativeMethods.EnumWindows(callback, 0);
        return sent;
    }

    public void Dispose()
    {
        if (_ownsMutex) { _ownsMutex = false; _mutex.ReleaseMutex(); }
        _mutex.Dispose();
    }
}
