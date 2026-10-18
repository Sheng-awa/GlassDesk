using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using GlassDesk.App;
using GlassDesk.Core;
using GlassDesk.NativeBridge;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace GlassDesk.VisualReview;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            Log("started: all media and artwork are in-memory test data");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(initializationArgs =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new ReviewApplication();
            });
        }
        catch (Exception error) { Log(error.ToString()); }
    }

    internal static void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "visual-review.log"), $"{DateTimeOffset.Now:O} {message}\n"); }
        catch { /* Logging must not keep the tool running or hide its original failure. */ }
    }
}

public sealed partial class ReviewApplication : Application
{
    ReviewWindow? _window;
    public ReviewApplication()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            Program.Log(args.Exception.ToString());
            args.Handled = true;
            _window?.Dispose();
            Exit();
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { _window = new ReviewWindow(Exit); _window.Activate(); }
        catch (Exception error) { Program.Log(error.ToString()); _window?.Dispose(); Exit(); }
    }
}

/// <summary>Isolated development harness. No media adapter, settings store, tray, or startup integration.</summary>
internal sealed class ReviewWindow : Window, IDisposable
{
    static readonly string[] CaseNames = ["长中英文字 · 横幅封面", "缺少封面 · 暂停", "竖幅封面 · 播放", "无音乐会话 · 真实时间"];
    readonly Action _exit;
    readonly DispatcherQueue _queue = DispatcherQueue.GetForCurrentThread();
    readonly UISettings _uiSettings = new();
    readonly Stopwatch _uptime = Stopwatch.StartNew();
    readonly ComboBox _cases = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ToggleSwitch _reduced = new() { Header = "减少动画", OnContent = "", OffContent = "" };
    readonly Button _play = new() { Content = "暂停测试曲目" };
    readonly TextBlock _status = Text("正在创建真实组件…", 13);
    readonly MediaSnapshot[] _templates;
    NativeWidgetHost? _host;
    WidgetScene? _scene;
    WidgetController? _controller;
    DispatcherQueueTimer? _motionTimer, _contentTimer;
    WidgetPreferences _preferences = new() { OnboardingSeen = true, AvoidFullscreen = false };
    MediaSnapshot _media = MediaSnapshot.Disconnected;
    int _caseIndex, _pressedId = -1;
    long _version;
    uint _dpi;
    bool _disposed, _syncUi, _down, _seeking;
    double Now => _uptime.Elapsed.TotalMilliseconds;
    NativeWidgetHost Host => _host ?? throw new InvalidOperationException("The test host is unavailable.");
    WidgetScene Scene => _scene ?? throw new InvalidOperationException("The test scene is unavailable.");
    WidgetController Controller => _controller ?? throw new InvalidOperationException("The test controller is unavailable.");
    WidgetPreferences EffectivePreferences => _preferences with { ReducedMotion = _preferences.ReducedMotion || !_uiSettings.AnimationsEnabled };

    public ReviewWindow(Action exit)
    {
        _exit = exit;
        _templates = CreateTestCases();
        Title = "GlassDesk 视觉验收 · 测试数据";
        BuildPanel();
        Closed += (_, _) => { Dispose(); _exit(); };
        try
        {
            SystemBackdrop = new MicaBackdrop();
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            var panelHwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            double scale = Math.Max(96u, GetDpiForWindow(panelHwnd)) / 96d;
            AppWindow.Resize(new((int)(520 * scale), Math.Min((int)(500 * scale), area.WorkArea.Height - 48)));
            AppWindow.Move(new(area.WorkArea.X + (int)(28 * scale), area.WorkArea.Y + (int)(72 * scale)));
            _host = new NativeWidgetHost();
            SetWindowTextW(Host.Handle, "GlassDesk 视觉验收 · 测试数据 · 组件");
            _scene = new WidgetScene(Host);
            _dpi = Host.Context.Dpi;
            var monitor = ReadMonitor();
            _controller = new WidgetController(EffectivePreferences, monitor,
                new WidgetAnchor(monitor.WorkArea.X + monitor.WorkArea.Width * .76, monitor.WorkArea.Y + 104 * monitor.Scale));
            _motionTimer = _queue.CreateTimer();
            _motionTimer.Interval = TimeSpan.FromMilliseconds(16);
            _motionTimer.Tick += (_, _) => Guard(() => { Controller.Tick(Now); if (!Controller.NeedsTick) _motionTimer!.Stop(); });
            _contentTimer = _queue.CreateTimer();
            _contentTimer.Tick += (_, _) => Guard(() =>
            {
                if (_media.Connected) Scene.UpdateTimeline(_media, Stopwatch.GetTimestamp());
                else Render();
            });
            Controller.FormChanged += _ => { Render(); WakeMotion(); };
            Controller.PresentationChanged += ApplyPresentation;
            Controller.ActionRequested += InvokeAction;
            Host.PointerEvent += e => Guard(() => OnPointer(e));
            Host.WindowContextChanged += context =>
            {
                if (_disposed || context.Dpi == _dpi) return;
                _dpi = context.Dpi;
                _queue.TryEnqueue(() => Guard(RefreshMonitor));
            };
            Host.GraphicsDeviceRestored += () => Guard(Render);
            Host.NativeError += message => { Program.Log(message); _status.Text = "原生渲染错误，详细信息见 visual-review.log。"; };
            SelectCase(0);
            ApplyPresentation(Controller.Motion.Current);
            Host.SetVisible(true);
            Program.Log("ready: native host + WidgetScene + WidgetController");
        }
        catch { Dispose(); throw; }
    }

    void BuildPanel()
    {
        var panel = new StackPanel { Spacing = 18, Padding = new Thickness(24) };
        panel.Children.Add(Text("视觉验收", 24, true));
        panel.Children.Add(Text("测试数据", 14, true));
        panel.Children.Add(Text("音乐、封面与进度均为本工具生成的测试数据。播放控制只改变此窗口中的测试状态。无会话案例显示本机的真实时间。", 13));
        panel.Children.Add(Text("检查文字省略、封面裁切、播放按钮反馈与可中途打断的三态过渡。也可以直接悬停、点击、拖动组件。", 13));
        foreach (string name in CaseNames) _cases.Items.Add(name);
        _cases.SelectionChanged += (_, _) => { if (!_syncUi && _cases.SelectedIndex >= 0) Guard(() => SelectCase(_cases.SelectedIndex)); };
        AutomationProperties.SetName(_cases, "选择视觉验收测试案例");
        panel.Children.Add(_cases);
        var forms = new Grid { ColumnSpacing = 8 };
        for (int index = 0; index < 3; index++)
        {
            forms.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var form = (WidgetForm)index;
            var button = new Button { Content = new[] { "胶囊", "悬停", "详情" }[index], HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => Guard(() => { Controller.ChangeForm(form, Now); WakeMotion(); });
            Grid.SetColumn(button, index); forms.Children.Add(button);
        }
        panel.Children.Add(forms);
        _play.Click += (_, _) => Guard(TogglePlayback);
        panel.Children.Add(_play);
        _reduced.Toggled += (_, _) => Guard(() =>
        {
            if (_controller is null) return;
            _preferences = _preferences with { ReducedMotion = _reduced.IsOn };
            Controller.UpdatePreferences(EffectivePreferences, Now);
            Render(); ApplyPresentation(Controller.Motion.Sample(Now)); WakeMotion();
        });
        panel.Children.Add(_reduced);
        panel.Children.Add(_status);
        var quit = new Button { Content = "退出视觉验收" };
        quit.Click += (_, _) => Close();
        panel.Children.Add(quit);
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled };
    }

    void SelectCase(int index)
    {
        _caseIndex = Math.Clamp(index, 0, _templates.Length - 1);
        var template = _templates[_caseIndex];
        _media = template with
        {
            ContentVersion = ++_version,
            Timeline = template.Timeline is { } timeline ? timeline with { ReceivedTimestamp = Stopwatch.GetTimestamp() } : null
        };
        _seeking = _down = false;
        Scene.SetSeekPreview(null);
        Scene.SetPointerFeedback(-1);
        Render();
        UpdatePanelState();
        Program.Log($"case: {_caseIndex} {CaseNames[_caseIndex]}");
    }

    void UpdatePanelState()
    {
        _syncUi = true;
        _cases.SelectedIndex = _caseIndex;
        _syncUi = false;
        _play.IsEnabled = _media.Connected && _media.Capabilities.Toggle;
        _play.Content = _media.State == PlaybackState.Playing ? "暂停测试曲目" : "播放测试曲目";
        _status.Text = $"测试案例：{CaseNames[_caseIndex]} · " + (_media.Connected ? (_media.State == PlaybackState.Playing ? "播放中" : "已暂停") : "无音乐会话");
    }

    void TogglePlayback()
    {
        if (!_media.Connected || !_media.Capabilities.Toggle || _media.Timeline is not { } timeline) return;
        _media = _media with
        {
            State = _media.State == PlaybackState.Playing ? PlaybackState.Paused : PlaybackState.Playing,
            ContentVersion = ++_version,
            Timeline = timeline with { Position = WidgetScene.PositionAt(_media, Stopwatch.GetTimestamp()), ReceivedTimestamp = Stopwatch.GetTimestamp() }
        };
        Render(); UpdatePanelState();
    }

    void InvokeAction(int id)
    {
        if (id == WidgetActions.Toggle) TogglePlayback();
        else if (id == WidgetActions.Previous && _media.Capabilities.Previous) SelectCase((_caseIndex + 2) % 3);
        else if (id == WidgetActions.Next && _media.Capabilities.Next) SelectCase((_caseIndex + 1) % 3);
        else if (id is WidgetActions.OpenMusic or WidgetActions.Settings) Activate();
    }

    void OnPointer(HostPointerEvent e)
    {
        int id = e.Kind is HostEventKind.Invoke or HostEventKind.RangeValue ? e.ControlId : Scene.HitTest(e.X, e.Y);
        if (e.Kind == HostEventKind.Quit) { Close(); return; }
        if (e.Kind == HostEventKind.OpenSettings) { Activate(); return; }
        if (e.Kind is HostEventKind.DpiChanged or HostEventKind.DisplayChanged) { RefreshMonitor(); return; }
        if (e.Kind == HostEventKind.Wheel) { Scene.ScrollDetails(-(float)e.Value * 40); return; }
        if (e.Kind == HostEventKind.RangeValue)
        {
            var control = Scene.GetAccessibleControls().FirstOrDefault(c => c.Id == WidgetActions.Seek);
            if (control is { Enabled: true }) Seek(Scene.SeekPositionFor(control.X + control.Width * (float)Math.Clamp(e.Value / 100, 0, 1)));
            return;
        }
        if (e.Kind == HostEventKind.Down)
        {
            _down = true; _pressedId = id;
            Scene.SetPointerFeedback(id, true);
            _seeking = id == WidgetActions.Seek && _media.Timeline?.Valid == true && _media.Capabilities.Seek;
            if (_seeking) Scene.SetSeekPreview(Scene.SeekPositionFor(e.X));
        }
        else if (e.Kind == HostEventKind.Move)
        {
            if (_seeking) Scene.SetSeekPreview(Scene.SeekPositionFor(e.X));
            else Scene.SetPointerFeedback(id, _down && id == _pressedId);
        }
        else if (e.Kind == HostEventKind.Up)
        {
            _down = false;
            Scene.SetPointerFeedback(id);
            if (_seeking) { _seeking = false; Seek(Scene.SeekPositionFor(e.X)); Scene.SetSeekPreview(null); }
        }
        else if (e.Kind is HostEventKind.Leave or HostEventKind.CaptureLost)
        {
            Scene.SetPointerFeedback(-1);
            if (e.Kind == HostEventKind.CaptureLost) { _down = _seeking = false; Scene.SetSeekPreview(null); }
        }
        Controller.HandlePointer(e, id, Now);
        WakeMotion();
    }

    void Seek(TimeSpan? position)
    {
        if (position is null || _media.Timeline is not { } timeline) return;
        var clamped = TimelineProjection.ClampSeek(_media, position.Value);
        if (clamped is null) return;
        _media = _media with { Timeline = timeline with { Position = clamped.Value, ReceivedTimestamp = Stopwatch.GetTimestamp() } };
        Scene.UpdateTimeline(_media, Stopwatch.GetTimestamp());
    }

    void Render()
    {
        if (_disposed) return;
        Scene.SetDetailsViewport(Math.Min(464, Controller.Monitor.WorkArea.Height / Controller.Monitor.Scale));
        Scene.Render(Controller.Form, _media, DateTimeOffset.Now, EffectivePreferences, "视觉验收 · 测试数据");
        bool needsContent = !_media.Connected || Controller.Form == WidgetForm.Details && _media.State == PlaybackState.Playing;
        _contentTimer!.Interval = _media.Connected ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(1);
        if (needsContent) _contentTimer!.Start(); else _contentTimer!.Stop();
    }

    void ApplyPresentation(WidgetPose pose)
    {
        if (_disposed) return;
        Host.ApplyFrame(new HostFrame(pose.Width, pose.Height, pose.Radius, _preferences.Blur, _preferences.TintOpacity, !_uiSettings.AdvancedEffectsEnabled));
        Host.SetPlacement((int)Math.Round(pose.X), (int)Math.Round(pose.Y));
        Scene.ApplyPresentation(pose);
    }
    void WakeMotion() { if (!_disposed && Controller.NeedsTick && !_motionTimer!.IsRunning) _motionTimer!.Start(); }
    void RefreshMonitor() { Controller.SetMonitor(ReadMonitor(), Now); Render(); WakeMotion(); }
    MonitorDescriptor ReadMonitor()
    {
        var area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(Host.Handle), DisplayAreaFallback.Nearest);
        return new("visual-review-display", new(area.OuterBounds.X, area.OuterBounds.Y, area.OuterBounds.Width, area.OuterBounds.Height),
            new(area.WorkArea.X, area.WorkArea.Y, area.WorkArea.Width, area.WorkArea.Height), Math.Max(96u, Host.Context.Dpi) / 96f, area.IsPrimary);
    }
    void Guard(Action action)
    {
        if (_disposed) return;
        try { action(); }
        catch (Exception error)
        {
            _motionTimer?.Stop(); _contentTimer?.Stop();
            _status.Text = "运行错误，已停止更新。详细信息见 visual-review.log。";
            Program.Log(error.ToString());
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _motionTimer?.Stop(); _contentTimer?.Stop();
        try { _scene?.Dispose(); } catch (Exception error) { Program.Log(error.ToString()); }
        try { _host?.Dispose(); } catch (Exception error) { Program.Log(error.ToString()); }
        Program.Log("closed");
    }

    static TextBlock Text(string text, double size, bool semibold = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
    };
    static MediaSnapshot[] CreateTestCases()
    {
        MediaSnapshot Music(string token, string title, string artist, byte[]? cover, PlaybackState state) => new(
            "visual-review-only-" + token, 0, true, title, artist, "测试专辑 · Original grid fixtures", cover, state, 1,
            new(TimeSpan.Zero, TimeSpan.FromSeconds(242), TimeSpan.FromSeconds(68), TimeSpan.Zero, TimeSpan.FromSeconds(242), Stopwatch.GetTimestamp()), new(true, true, true, true));
        return
        [
            Music("landscape", "[测试] 穿过漫长的冬夜，抵达另一端的晨光 / A very long title for precise ellipsis and multilingual typography", "测试艺人 · Original Test Artist with a deliberately long name", GridPng(640, 360), PlaybackState.Playing),
            Music("no-cover", "[测试] A quiet evening beyond the horizon — an intentionally long English title with 中文副标题", "测试艺人 / Missing Artwork Case", null, PlaybackState.Paused),
            Music("portrait", "[测试] 蓝与绿之间 / Between Blue and Green", "测试艺人 · Portrait Grid", GridPng(360, 640), PlaybackState.Playing),
            MediaSnapshot.Disconnected with { SessionToken = "visual-review-only-no-session", Notice = "视觉验收 · 测试数据" }
        ];
    }

    /// <summary>Original RGB PNG grid. Encoded in memory; no file, network, or third-party artwork.</summary>
    static byte[] GridPng(int width, int height)
    {
        uint[] palette = [0x224A58, 0x3D797E, 0x87B4AA, 0xD6D7BD, 0xD69C75, 0x935B50];
        using var rows = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            rows.WriteByte(0);
            for (int x = 0; x < width; x++)
            {
                int column = x * 8 / width, row = y * 8 / height;
                uint color = palette[(column + row) % palette.Length];
                if (x % Math.Max(1, width / 8) < 3 || y % Math.Max(1, height / 8) < 3) color = 0xF4F1E8;
                double distance = Math.Pow((x - width / 2d) / (width * .21), 2) + Math.Pow((y - height / 2d) / (height * .21), 2);
                if (distance is > .86 and < 1.14) color = 0x172F38;
                rows.WriteByte((byte)(color >> 16)); rows.WriteByte((byte)(color >> 8)); rows.WriteByte((byte)color);
            }
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) rows.WriteTo(zlib);
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; header[9] = 2;
        Chunk("IHDR", header); Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
        return png.ToArray();

        void Chunk(string type, byte[] data)
        {
            Span<byte> value = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(value, data.Length); png.Write(value);
            byte[] name = System.Text.Encoding.ASCII.GetBytes(type); png.Write(name); png.Write(data);
            uint crc = 0xFFFFFFFF;
            foreach (byte item in name.Concat(data))
            {
                crc ^= item;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
            }
            BinaryPrimitives.WriteUInt32BigEndian(value, ~crc); png.Write(value);
        }
    }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowTextW(nint hwnd, string text);
}
