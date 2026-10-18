using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GlassDesk.LifecycleProbe;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(initializationArgs =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new ProbeApp();
        });
    }
}
internal sealed class ProbeApp : Application
{
    readonly NativeClick callback;
    IntPtr native;
    Window? settings;
    int openings;
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate void NativeClick();
    [DllImport("GlassDesk.ProbeNative.dll", CallingConvention=CallingConvention.StdCall)] static extern int GDProbeCreate(NativeClick callback, out IntPtr context, out IntPtr hwnd);
    [DllImport("GlassDesk.ProbeNative.dll", CallingConvention=CallingConvention.StdCall)] static extern void GDProbeDestroy(IntPtr context);
    internal ProbeApp() { callback = ShowSettings; }
    void Log(string text) => File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"lifecycle-managed.log"),DateTimeOffset.Now.ToString("O")+" "+text+Environment.NewLine);
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Marshal.ThrowExceptionForHR(GDProbeCreate(callback,out native,out var hwnd));
        Log($"native-created {hwnd}");
        settings=new Window {Title="GlassDesk P0 Settings"};
        var panel=new StackPanel {Spacing=18, Padding=new Thickness(24)};
        panel.Children.Add(new TextBlock {Text="WinUI 与原生玻璃宿主生命周期",FontSize=24});
        panel.Children.Add(new TextBlock {Text="关闭设置后，点击玻璃窗口再次打开设置。",TextWrapping=TextWrapping.Wrap});
        var hide=new Button {Content="关闭设置，继续驻留"};
        hide.Click+=(_,_)=>HideSettings(); panel.Children.Add(hide);
        var exit=new Button {Content="退出生命周期探针"};
        exit.Click+=(_,_)=>{GDProbeDestroy(native);native=IntPtr.Zero;Log("exit-clean");Exit();};panel.Children.Add(exit);
        settings.Content=panel;
        settings.AppWindow.Resize(new Windows.Graphics.SizeInt32(680,360));
        settings.AppWindow.Closing+=(_,e)=>{e.Cancel=true;HideSettings();};
        ShowSettings();
    }
    void HideSettings(){settings!.AppWindow.Hide();Log("settings-hidden; native-alive");}
    void ShowSettings(){if(settings is null)return;openings++;settings.Title=$"GlassDesk P0 Settings — opens {openings}";settings.AppWindow.Show();Log($"settings-shown {openings}");}
}
