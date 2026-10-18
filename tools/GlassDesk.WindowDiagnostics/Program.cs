using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;
Native.SetProcessDpiAwarenessContext((nint)(-4));
var hwnd=nint.Parse(args[0]);
if(!Native.IsWindow(hwnd)) {Console.Error.WriteLine("Target HWND is unavailable on this desktop.");return 1;}
Native.GetWindowRect(hwnd,out var rect);
Native.GetWindowThreadProcessId(hwnd,out var pid);
Native.GetCursorPos(out var cursor);
uint? captureAffinity=Native.GetWindowDisplayAffinity(hwnd,out var observedAffinity)?observedAffinity:null;
var hitPoint=new Native.Point {X=args.Length>2?int.Parse(args[2]):cursor.X,Y=args.Length>3?int.Parse(args[3]):cursor.Y};
var hitWindow=Native.WindowFromPoint(hitPoint);
var hitResult=Native.SendMessageW(hwnd,0x84,0,(nint)((hitPoint.Y<<16)|(hitPoint.X&0xffff)));
var context=Native.GetWindowDpiAwarenessContext(hwnd);
Native.GetDpiForMonitor(Native.MonitorFromWindow(hwnd,2),0,out var monitorX,out var monitorY);
using var process=Process.GetProcessById((int)pid);
// Process.Handle requests excessive rights. Resource queries only need query access.
var queryHandle=Native.OpenProcess(0x0400,false,pid);
try {
var report=JsonSerializer.Serialize(new{Hwnd=(long)hwnd,Pid=pid,Visible=Native.IsWindowVisible(hwnd),CaptureAffinity=captureAffinity,WindowStyle=(long)Native.GetWindowLongPtrW(hwnd,-16),Dpi=Native.GetDpiForWindow(hwnd),MonitorDpi=monitorX,SystemDpi=Native.GetDpiForSystem(),Awareness=Native.GetAwarenessFromDpiAwarenessContext(context),PerMonitorV2=Native.AreDpiAwarenessContextsEqual(context,(nint)(-4)),Style=(long)Native.GetWindowLongPtrW(hwnd,-20),Foreground=(long)Native.GetForegroundWindow(),Cursor=cursor,HitPoint=hitPoint,HitWindow=(long)hitWindow,HitResult=(long)hitResult,Rect=rect,PrivateBytes=process.PrivateMemorySize64,Handles=process.HandleCount,Gdi=queryHandle==0?(uint?)null:Native.GetGuiResources(queryHandle,0),User=queryHandle==0?(uint?)null:Native.GetGuiResources(queryHandle,1)},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true});
Console.WriteLine(report);
if(args.Length>1)File.WriteAllText(Path.GetFullPath(args[1]),report);
} finally {if(queryHandle!=0)Native.CloseHandle(queryHandle);}
return 0;
static class Native
{
    [StructLayout(LayoutKind.Sequential)]public struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]public struct Point{public int X,Y;}
    [DllImport("user32.dll")]public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll",SetLastError=true)]public static extern bool GetWindowDisplayAffinity(nint hwnd,out uint affinity);
    [DllImport("user32.dll")]public static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")]public static extern nint SendMessageW(nint window,uint message,nint word,nint value);
    [DllImport("user32.dll")]public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")]public static extern uint GetDpiForSystem();
    [DllImport("user32.dll")]public static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")]public static extern nint MonitorFromWindow(nint hwnd,uint flags);
    [DllImport("shcore.dll")]public static extern int GetDpiForMonitor(nint monitor,int kind,out uint x,out uint y);
    [DllImport("user32.dll")]public static extern nint GetWindowDpiAwarenessContext(nint hwnd);
    [DllImport("user32.dll")]public static extern int GetAwarenessFromDpiAwarenessContext(nint context);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]public static extern bool AreDpiAwarenessContextsEqual(nint a,nint b);
    [DllImport("user32.dll")]public static extern nint GetWindowLongPtrW(nint hwnd,int index);
    [DllImport("user32.dll")]public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]public static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
    [DllImport("user32.dll")]public static extern uint GetGuiResources(nint process,uint flag);
    [DllImport("user32.dll")]public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")]public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("kernel32.dll")]public static extern nint OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll")]public static extern bool CloseHandle(nint handle);
}
