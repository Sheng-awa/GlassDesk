using System.Runtime.InteropServices;
using System.Text.Json;
using GlassDesk.Core;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace GlassDesk.App;

/// <summary>One reusable light settings window; normal Close hides it.</summary>
public sealed class SettingsWindow : Window, IDisposable
{
    const uint CanvasColor = 0x50FFFFFF;
    const uint SurfaceColor = 0x45FFFFFF;
    const uint BorderColor = 0x78FFFFFF;
    const uint ButtonColor = 0x86FFFFFF;
    const uint InkColor = 0xFF20252C;
    const double ContentWidth = 760;
    static readonly AccessibilitySettings HighContrast = new();
    static readonly UISettings SystemColors = new();
    readonly UISettings _uiSettings = new();
    readonly ISettingsCoordinator _coordinator;
    readonly DispatcherQueueTimer _changeTimer;
    readonly DispatcherQueueTimer _statusTimer;
    readonly Action<string>? _trace;
    readonly bool _minimalContent;
    FrameworkElement? _tracedRoot;
    readonly Slider _blur = new() { Minimum = 0, Maximum = 40, StepFrequency = 1 };
    readonly Slider _transparency = new() { Minimum = 10, Maximum = 90, StepFrequency = 1 };
    readonly Slider _hover = new() { Minimum = 100, Maximum = 1500, StepFrequency = 50 };
    readonly Slider _leave = new() { Minimum = 100, Maximum = 1500, StepFrequency = 50 };
    readonly ToggleSwitch _reducedMotion = new() { OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
    readonly ToggleSwitch _fullscreen = new() { OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
    readonly ToggleSwitch _startup = new() { OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
    readonly ToggleSwitch _audio = new() { OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
    readonly ToggleSwitch _companion = new() { OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
    readonly ToggleSwitch _companionFollowMouse = new() { OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
    readonly ToggleSwitch _liquidGlass = new() { OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
    readonly ToggleSwitch _clockTime = new() { OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
    readonly ToggleSwitch _clockDate = new() { OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
    readonly ToggleSwitch _clockWeekday = new() { OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
    readonly TextBlock _audioStatus = Description();
    readonly TextBlock _liquidGlassStatus = Description();
    readonly StackPanel _pluginList = new() { Spacing = 12 };
    readonly StackPanel _pluginPages = new() { Spacing = 12 };
    readonly TextBox _customColor = new() { PlaceholderText = "#202329", MinWidth = 0 };
    readonly ColorPicker _colorPicker = new()
    {
        IsAlphaEnabled = false, IsAlphaSliderVisible = false, IsAlphaTextInputVisible = false,
        IsColorSliderVisible = true, IsColorChannelTextInputVisible = true, IsHexInputVisible = false,
        IsColorPreviewVisible = true, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch
    };
    readonly ComboBox _companionForm = new() { MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _colorFeedback = Description();
    readonly Microsoft.UI.Xaml.Shapes.Path _companionPreview = new() { StrokeThickness = 1 };
    readonly Microsoft.UI.Xaml.Shapes.Ellipse _previewLeftEye = new() { Width = 8, Height = 12 };
    readonly Microsoft.UI.Xaml.Shapes.Ellipse _previewRightEye = new() { Width = 8, Height = 12 };
    SettingsFloatingNavigation? _tabs;
    SettingsAcrylicBackdrop? _acrylicBackdrop;
    Grid? _floatingRoot;
    Grid? _settingsPane;
    XamlRoot? _shapeXamlRoot;
    SettingsWindowShape? _windowShape;
    string? _pluginsKey;
    readonly TextBlock _blurLabel = Label();
    readonly TextBlock _transparencyLabel = Label();
    readonly TextBlock _hoverLabel = Label();
    readonly TextBlock _leaveLabel = Label();
    readonly TextBlock _connection = Label();
    readonly TextBlock _capabilities = Description();
    readonly TextBlock _status = Description();
    readonly TextBlock _hotkeyHint = Description("选中输入框，按下组合键即可绑定。");
    readonly TextBlock _connectionHelp = Description("请在网易云音乐中播放一首歌。若仍未连接，请在音乐设置中开启 SMTC（系统媒体控制）。");
    readonly TextBlock _visibilityText = Label();
    readonly TextBox _hotkey = new() { IsReadOnly = true, PlaceholderText = "未绑定", MinWidth = 220 };
    readonly Button _visibility = new() { Content = "隐藏组件" };
    readonly Border _onboarding = new() { Visibility = Visibility.Collapsed };
    readonly InfoBar _feedback = new() { IsOpen = false, IsClosable = true };
    readonly TextBox _diagnosticPath = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    WidgetPreferences? _pending;
    WidgetPreferences? _inFlight;
    TaskCompletionSource? _updateCompletion;
    bool _refreshing;
    bool _shutdown;
    bool _disposed;
    bool _updating;
    bool _shown;
    bool _hexDirty, _writingHex;
    long _appearanceRevision, _pendingAppearanceRevision;
    (uint Color, string Id)? _previewAppearance;

    public SettingsWindow(ISettingsCoordinator coordinator) : this(coordinator, minimalContent: false, useBackdrop: true) { }

    internal SettingsWindow(ISettingsCoordinator coordinator, bool minimalContent, bool useBackdrop, Action<string>? trace = null)
    {
        _coordinator = coordinator;
        _minimalContent = minimalContent;
        _trace = trace;
        DispatcherQueueTimer? timer = null;
        DispatcherQueueTimer? statusTimer = null;
        try
        {
            Title = _coordinator.IsUiPreview ? "GlassDesk UI Preview 设置" : "GlassDesk 设置";
            Trace("settings-window-created");
            _changeTimer = timer = DispatcherQueue.CreateTimer();
            _changeTimer.Interval = TimeSpan.FromMilliseconds(250);
            _changeTimer.IsRepeating = false;
            _changeTimer.Tick += OnChangeTimerTick;
            _statusTimer = statusTimer = DispatcherQueue.CreateTimer();
            _statusTimer.Interval = TimeSpan.FromSeconds(1);
            _statusTimer.IsRepeating = true;
            _statusTimer.Tick += OnStatusTimerTick;
            if (minimalContent) BuildMinimalContent();
            else BuildContent();
            Trace(minimalContent ? "settings-content-minimal" : "settings-content-full");
            _tracedRoot = (FrameworkElement)Content;
            _tracedRoot.Loaded += OnContentLoaded;
            _tracedRoot.LayoutUpdated += OnFirstLayoutUpdated;
            // The XAML root must exist before WinUI connects the backdrop to its target.
            if (useBackdrop && _floatingRoot is not null && _settingsPane is not null && _tabs is not null)
                SystemBackdrop = _acrylicBackdrop = new SettingsAcrylicBackdrop(CurrentPreferences.TintOpacity);
            Trace(useBackdrop ? "settings-backdrop-connected" : "settings-backdrop-disabled");
            if (!minimalContent) BindEvents();
            ResizeToWorkArea();
            Refresh();
            _coordinator.Changed += Refresh;
            AppWindow.Closing += OnAppWindowClosing;
            Closed += OnWindowClosed;
            Trace("settings-construction-complete");
        }
        catch (Exception error)
        {
            Trace($"settings-construction-error:{error}");
            _shutdown = true;
            timer?.Stop();
            if (timer is not null) timer.Tick -= OnChangeTimerTick;
            statusTimer?.Stop();
            if (statusTimer is not null) statusTimer.Tick -= OnStatusTimerTick;
            _coordinator.Changed -= Refresh;
            AppWindow.Closing -= OnAppWindowClosing;
            Closed -= OnWindowClosed;
            DetachContentTrace();
            try { SystemBackdrop = null; Content = null; Close(); }
            catch { /* Preserve the original resource initialization failure. */ }
            _disposed = true;
            throw;
        }
    }

    void Trace(string message)
    {
        try { _trace?.Invoke(message); }
        catch { /* Optional QA tracing must not change the window's behavior. */ }
    }

    void OnContentLoaded(object sender, RoutedEventArgs args)
    {
        Trace("settings-content-loaded");
        if (_floatingRoot?.XamlRoot is { } xamlRoot && _shapeXamlRoot != xamlRoot)
        {
            if (_shapeXamlRoot is not null) _shapeXamlRoot.Changed -= OnSettingsXamlRootChanged;
            _shapeXamlRoot = xamlRoot; xamlRoot.Changed += OnSettingsXamlRootChanged;
        }
        UpdateWindowShape();
    }

    void OnSettingsXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateWindowShape();

    void UpdateWindowShape()
    {
        if (_disposed || _floatingRoot is null || _settingsPane is null || _tabs is null) return;
        _windowShape ??= new SettingsWindowShape(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (!_windowShape.Update(_floatingRoot, _settingsPane, _tabs)) Trace("settings-shape-unavailable");
    }
    void OnFirstLayoutUpdated(object? sender, object args)
    {
        if (_tracedRoot is null || _tracedRoot.ActualWidth <= 0 || _tracedRoot.ActualHeight <= 0) return;
        Trace($"settings-first-layout:{_tracedRoot.ActualWidth:0.##}x{_tracedRoot.ActualHeight:0.##}");
        _tracedRoot.LayoutUpdated -= OnFirstLayoutUpdated;
    }

    void DetachContentTrace()
    {
        if (_tracedRoot is null) return;
        _tracedRoot.Loaded -= OnContentLoaded;
        _tracedRoot.LayoutUpdated -= OnFirstLayoutUpdated;
        _tracedRoot = null;
    }

    async void OnChangeTimerTick(DispatcherQueueTimer sender, object args) => await FlushPreferencesAsync();
    void OnStatusTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || !_shown || _minimalContent || !AppWindow.IsVisible)
        {
            sender.Stop();
            return;
        }
        RefreshRuntimeStatus();
    }
    void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdown) return;
        args.Cancel = true;
        Hide();
    }
    void OnWindowClosed(object sender, WindowEventArgs args) => Detach();

    public void Show()
    {
        if (_disposed) return;
        Refresh();
        AppWindow.Show();
        _shown = true;
        _tabs?.Resume();
        UpdateWindowShape();
        if (!_minimalContent) _statusTimer.Start();
        Trace("settings-appwindow-shown");
        Activate();
        Trace("settings-window-activated");
    }

    public void ShowPlugins()
    {
        Show();
        _tabs?.Select(3);
    }

    void BuildMinimalContent()
    {
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(28) };
        panel.Children.Add(SettingsTypography.Apply(new TextBlock { Text = "GlassDesk" }, TextRole.PageTitle, InkRole.Primary));
        panel.Children.Add(Description("设置窗口验收"));
        var close = new Button { Content = "关闭设置" };
        SettingsTypography.Apply(close, TextRole.Label, InkRole.Primary);
        close.Click += (_, _) => Hide();
        panel.Children.Add(close);
        var root = new Grid { RequestedTheme = ElementTheme.Light, Background = Brush(0xFFF6F8FB) };
        root.Children.Add(panel);
        Content = root;
    }

    public void Hide()
    {
        if (_disposed) return;
        _shown = false;
        _tabs?.Pause();
        _statusTimer.Stop();
        AppWindow.Hide();
        _ = FlushPendingAsync();
    }

    void BuildContent()
    {
        var root = _floatingRoot = new Grid { RequestedTheme = ElementTheme.Light, Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var pane = _settingsPane = new Grid
        {
            CornerRadius = new CornerRadius(24), Margin = new Thickness(16, 0, 16, 16),
            Background = Brush(SettingsAcrylicBackdrop.IsSupported ? CanvasColor : 0xFFF0F2F5),
            BorderBrush = Brush(BorderColor), BorderThickness = new Thickness(1)
        };
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(pane, 1); root.Children.Add(pane);
        var header = new Grid { Padding = new Thickness(24, 16, 16, 12), ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleDragRegion = new Grid { Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), MinHeight = 40 };
        titleDragRegion.Children.Add(SettingsTypography.Apply(new TextBlock
        {
            Text = "GlassDesk", VerticalAlignment = VerticalAlignment.Center
        }, TextRole.PageTitle, InkRole.Primary));
        header.Children.Add(titleDragRegion);
        titleDragRegion.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(titleDragRegion).Properties.IsLeftButtonPressed) return;
            args.Handled = true;
            SettingsWindowShape.BeginDrag(WinRT.Interop.WindowNative.GetWindowHandle(this));
        };
        var visibilityContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        visibilityContent.Children.Add(new FontIcon { Glyph = "\uE890", FontSize = 16 });
        SettingsTypography.Apply(_visibilityText, TextRole.Label, InkRole.Primary);
        visibilityContent.Children.Add(_visibilityText);
        _visibility.Content = visibilityContent;
        _visibility.MinHeight = 36;
        _visibility.Padding = new Thickness(12, 6, 12, 6);
        _visibility.CornerRadius = new CornerRadius(24);
        _visibility.Background = Brush(ButtonColor); _visibility.BorderBrush = Brush(BorderColor);
        _visibility.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_visibility, 1);
        header.Children.Add(_visibility);
        var closePane = new Button
        {
            Content = new FontIcon { Glyph = "\uE8BB", FontSize = 14 }, Width = 40, Height = 40, MinWidth = 40,
            Padding = new Thickness(0), CornerRadius = new CornerRadius(20), Background = Brush(ButtonColor), BorderBrush = Brush(BorderColor)
        };
        Name(closePane, "关闭设置"); ToolTipService.SetToolTip(closePane, "关闭设置"); closePane.Click += (_, _) => Hide();
        Grid.SetColumn(closePane, 2); header.Children.Add(closePane);
        pane.Children.Add(header);

        var body = new Grid();
        _tabs = new SettingsFloatingNavigation(["外观", "交互", "陪伴", "插件", "通用"], () => CurrentPreferences.ReducedMotion || !_uiSettings.AnimationsEnabled)
        { Margin = new Thickness(16, 16, 16, 12) };
        var content = new Grid
        {
            RowSpacing = 16, Padding = new Thickness(24, 12, 24, 12), MaxWidth = ContentWidth + 48,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(content);
        Grid.SetRow(body, 1); pane.Children.Add(body);

        var onboardingContent = new Grid { ColumnSpacing = 12 };
        onboardingContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        onboardingContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        onboardingContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        onboardingContent.Children.Add(new FontIcon
        {
            Glyph = "\uE946", FontSize = 16, Foreground = Brush(0xFF28786E), VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0)
        });
        var onboardingText = new StackPanel { Spacing = 4 };
        onboardingText.Children.Add(SettingsTypography.Apply(new TextBlock { Text = "使用提示" }, TextRole.CaptionSettings, InkRole.Primary));
        onboardingText.Children.Add(Description("悬停查看歌曲，点击展开播放器。拖动空白处调整位置，点击组件外部收起。"));
        Grid.SetColumn(onboardingText, 1); onboardingContent.Children.Add(onboardingText);
        var understood = new Button
        {
            Content = new FontIcon { Glyph = "\uE8BB", FontSize = 12 }, Width = 32, Height = 32,
            MinWidth = 32, Padding = new Thickness(0), Background = null, BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top
        };
        Name(understood, "知道了");
        ToolTipService.SetToolTip(understood, "知道了");
        understood.Click += (_, _) => QueuePreferences(CurrentPreferences with { OnboardingSeen = true });
        Grid.SetColumn(understood, 2); onboardingContent.Children.Add(understood);
        _onboarding.Child = onboardingContent;
        _onboarding.Padding = new Thickness(14, 12, 10, 12);
        _onboarding.CornerRadius = new CornerRadius(10);
        _onboarding.Background = Brush(0xFFEFF6F4);
        _onboarding.BorderBrush = Brush(0xFFD8E8E3);
        _onboarding.BorderThickness = new Thickness(1);
        content.Children.Add(_feedback);

        var appearance = Section("外观", "调整桌面组件的玻璃质感与动画。");
        appearance.Children.Add(new SettingsColumns([
            Group(SliderRow("模糊强度", _blurLabel, _blur)),
            Group(SliderRow("透明度", _transparencyLabel, _transparency, "透出背后的桌面")),
            Group(ToggleRow("简化动画", _reducedMotion), ToggleRow("真实音频波浪条", _audio)),
            Group(ToggleRow("显示时间", _clockTime), ToggleRow("显示日期", _clockDate), ToggleRow("显示星期", _clockWeekday))]));
        appearance.Children.Add(_audioStatus);
        var restore = CommandButton("恢复默认外观", "\uE777");
        restore.Click += (_, _) => QueuePreferences(CurrentPreferences with { Blur = 20, TintOpacity = .24f, ReducedMotion = false,
            ClockShowTime = true, ClockShowDate = false, ClockShowWeekday = false });
        appearance.Children.Add(restore);
        appearance.Children.Add(_onboarding);

        var interaction = Section("交互与位置", "调整悬停、离开与全屏时的行为。");
        interaction.Children.Add(new SettingsColumns([
            Group(SliderRow("悬停展开延迟", _hoverLabel, _hover), SliderRow("离开收起延迟", _leaveLabel, _leave)),
            Group(ToggleRow("全屏时自动隐藏", _fullscreen, "进入全屏应用时，组件会暂时隐藏。"))]));
        var resetPosition = CommandButton("重置位置", "\uE7C2");
        resetPosition.Click += (_, _) => { _coordinator.ResetPosition(); ShowFeedback("位置已重置。"); };
        interaction.Children.Add(resetPosition);
        interaction.Children.Add(Description("也可以直接拖动组件的空白区域。"));

        var companion = Section("陪伴", "安静的原创小球，用表情和视线回应你。");
        companion.Children.Add(Group(ToggleRow("陪伴球", _companion, "回应指针、音乐和展开动作，并自然眨眼；隐藏时休息。"),
            ToggleRow("陪伴球跟随鼠标", _companionFollowMouse, "收起时也能望向胶囊外的鼠标；关闭后立即停止跟随。")));
        var colors = new StackPanel { Spacing = 12, Padding = new Thickness(16) };
        colors.Children.Add(SettingTitle("球体颜色"));
        colors.Children.Add(Description("眼睛会自动使用清晰的浅色或深色。"));
        Name(_colorPicker, "陪伴颜色，RGB 不透明取色器");
        colors.Children.Add(_colorPicker);
        var customRow = new Grid { ColumnSpacing = 8 };
        customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Name(_customColor, "自定义球体颜色，六位十六进制 RGB");
        customRow.Children.Add(_customColor);
        var applyColor = CommandButton("应用", "\uE790");
        applyColor.Click += async (_, _) => { if (CommitHexColor()) await FlushPendingAsync(); };
        Grid.SetColumn(applyColor, 1); customRow.Children.Add(applyColor); WrapActions(customRow, _customColor, applyColor);
        colors.Children.Add(customRow);
        _colorFeedback.Visibility = Visibility.Collapsed; colors.Children.Add(_colorFeedback);
        var form = new StackPanel { Spacing = 12, Padding = new Thickness(16) };
        form.Children.Add(SettingTitle("形式"));
        _companionForm.Items.Add(new ComboBoxItem { Content = "原生", Tag = CompanionAppearanceIds.Original });
        _companionForm.Items.Add(new ComboBoxItem { Content = "猫娘", Tag = CompanionAppearanceIds.CatEar });
        _companionForm.Items.Add(new ComboBoxItem { Content = "恶魔", Tag = CompanionAppearanceIds.Devil });
        Name(_companionForm, "陪伴形式"); form.Children.Add(_companionForm);
        form.Children.Add(Description("猫娘使用宽根猫耳；恶魔使用向内收尖的弯角。保留当前颜色。"));
        var preview = new Canvas { Width = 96, Height = 100, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        preview.Children.Add(_companionPreview);
        Canvas.SetLeft(_previewLeftEye, 31.7); Canvas.SetTop(_previewLeftEye, 45.1);
        Canvas.SetLeft(_previewRightEye, 56.3); Canvas.SetTop(_previewRightEye, 45.1);
        preview.Children.Add(_previewLeftEye); preview.Children.Add(_previewRightEye);
        form.Children.Add(preview); form.Children.Add(Description("当前形式与颜色 · 静态预览"));
        companion.Children.Add(new SettingsColumns([Surface(colors), Surface(form)]));

        var plugins = Section("插件", "安装本地功能插件，分别控制各项贡献。");
        plugins.Children.Add(Group(ToggleRow("液态玻璃", _liquidGlass, "为外壳加入真实背景折射；内容保持清晰。")));
        plugins.Children.Add(_liquidGlassStatus);
        var import = CommandButton("导入本地插件", "\uE898");
        import.Click += async (_, _) =>
        {
            import.IsEnabled = false;
            try
            {
                await FlushPendingAsync();
                string? packagePath;
                try
                {
                    // App SDK pickers accept the owning WindowId directly and support
                    // elevated desktop processes, unlike the legacy UWP picker.
                    var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id);
                    picker.FileTypeFilter.Add(".glassdesk-plugin");
                    var file = await picker.PickSingleFileAsync();
                    packagePath = file?.Path;
                }
                catch (Exception error)
                {
                    Trace($"plugin-picker-unavailable:{error.GetType().Name}:0x{error.HResult:X8}");
                    ShowFeedback($"无法打开文件选择器（0x{error.HResult:X8}），请稍后重试。", error: true);
                    return;
                }
                if (packagePath is not null) ReportResult(await _coordinator.ImportPluginAsync(packagePath));
            }
            catch (Exception error)
            {
                Trace($"plugin-import-failed:{error.GetType().Name}:0x{error.HResult:X8}");
                ShowFeedback($"插件导入失败（0x{error.HResult:X8}），请检查文件后重试。", error: true);
            }
            finally { import.IsEnabled = true; }
        };
        plugins.Children.Add(import);
        plugins.Children.Add(Description("插件在程序内运行，请仅导入你信任的文件。更新同名插件会保留配置。"));
        plugins.Children.Add(_pluginList); plugins.Children.Add(_pluginPages);

        var startup = Section("启动与快捷键", "设置登录时启动与显示 / 隐藏快捷键。");
        startup.Children.Add(Group(ToggleRow("登录 Windows 后启动", _startup, "下次登录时自动启动 GlassDesk。")));
        var hotkeySettings = new StackPanel { Spacing = 10, Padding = new Thickness(16) };
        hotkeySettings.Children.Add(SettingTitle("显示 / 隐藏快捷键"));
        var binding = new Grid { ColumnSpacing = 12 };
        binding.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        binding.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        binding.Children.Add(_hotkey);
        _hotkey.MinWidth = 80;
        var clear = CommandButton("清除", "\uE894");
        _startup.IsEnabled = _hotkey.IsEnabled = clear.IsEnabled = !_coordinator.IsUiPreview;
        if (_coordinator.IsUiPreview)
        {
            startup.Children.Add(Description("UI 预览不修改系统启动和全局快捷键。"));
            _hotkeyHint.Text = "预览版保留配置显示，系统快捷键注册已隔离。";
        }
        clear.Click += (_, _) => QueuePreferences(CurrentPreferences with { HotkeyKey = 0, HotkeyModifiers = 0 });
        Grid.SetColumn(clear, 1); binding.Children.Add(clear);
        WrapActions(binding, _hotkey, clear);
        hotkeySettings.Children.Add(binding); hotkeySettings.Children.Add(_hotkeyHint);
        startup.Children.Add(Surface(hotkeySettings));

        var connection = Section("连接与关于", "查看音乐连接状态与 GlassDesk 信息。");
        var connectionSettings = new StackPanel { Spacing = 10, Padding = new Thickness(16) };
        SettingsTypography.Apply(_connection, TextRole.SectionTitle, InkRole.Primary);
        connectionSettings.Children.Add(_connection); connectionSettings.Children.Add(_capabilities); connectionSettings.Children.Add(_status);
        var connectionActions = new Grid { ColumnSpacing = 8 };
        connectionActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        connectionActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var openMusic = CommandButton("打开音乐", "\uE8D6");
        Name(openMusic, "打开网易云音乐");
        ToolTipService.SetToolTip(openMusic, "打开网易云音乐");
        openMusic.HorizontalAlignment = HorizontalAlignment.Stretch;
        openMusic.Click += (_, _) => _coordinator.OpenMusic();
        connectionActions.Children.Add(openMusic);
        var reconnect = CommandButton("重新连接", "\uE72C");
        reconnect.HorizontalAlignment = HorizontalAlignment.Stretch;
        reconnect.Click += async (_, _) =>
        {
            reconnect.IsEnabled = false;
            try { await _coordinator.RetryMediaAsync(); }
            catch { ShowFeedback("连接未能恢复，请稍后重试。", error: true); }
            finally { reconnect.IsEnabled = true; }
        };
        Grid.SetColumn(reconnect, 1);
        connectionActions.Children.Add(reconnect);
        WrapActions(connectionActions, openMusic, reconnect);
        connectionSettings.Children.Add(connectionActions);
        connectionSettings.Children.Add(_connectionHelp);
        connection.Children.Add(Surface(connectionSettings));

        var about = new StackPanel { Spacing = 10, Padding = new Thickness(16) };
        about.Children.Add(SettingTitle("GlassDesk"));
        string version = typeof(SettingsWindow).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "preview";
        about.Children.Add(Description($"版本 {version}{(_coordinator.IsUiPreview ? " · UI Preview" : "")} · Windows 11"));
        var export = CommandButton("导出诊断", "\uE896");
        export.Click += async (_, _) =>
        {
            export.IsEnabled = false;
            try
            {
                _diagnosticPath.Text = await _coordinator.ExportDiagnosticsAsync();
                _diagnosticPath.Visibility = Visibility.Visible;
                ShowFeedback("诊断已导出。");
            }
            catch (Exception) { ShowFeedback("诊断导出失败，请稍后重试。", error: true); }
            finally { export.IsEnabled = true; }
        };
        about.Children.Add(export); about.Children.Add(_diagnosticPath);
        about.Children.Add(Description("诊断用于排查运行与连接问题，不包含歌曲历史或账号信息。"));
        about.Children.Add(Separator());
        var quit = CommandButton("退出 GlassDesk", "\uE7E8");
        quit.Background = null; quit.BorderThickness = new Thickness(0);
        quit.Click += async (_, _) => { await FlushPendingAsync(); _coordinator.Quit(); };
        about.Children.Add(quit);
        connection.Children.Add(Surface(about));

        var general = Section("通用", "启动、快捷键、音乐连接与诊断。");
        // Keep the existing live-save and connection controls in one concise page.
        startup.Children.RemoveAt(0); connection.Children.RemoveAt(0);
        general.Children.Add(startup); general.Children.Add(connection);
        var sections = new[] { appearance, interaction, companion, plugins, general };
        var deck = new SettingsPageDeck(sections);
        Grid.SetRow(deck, 1); content.Children.Add(deck);
        _tabs.SelectionChanged += selected => deck.SetSelectedIndex(selected);
        _tabs.ProgressChanged += progress => deck.SetProgress(progress);

        var footer = new Grid { Padding = new Thickness(22, 10, 22, 14), ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var saveHint = Description("更改会自动保存");
        saveHint.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(saveHint);
        var hideSettings = new Button { Content = "关闭设置", CornerRadius = new CornerRadius(24), MinHeight = 40,
            Background = Brush(ButtonColor), BorderBrush = Brush(BorderColor), Padding = new Thickness(16, 6, 16, 6) };
        SettingsTypography.Apply(hideSettings, TextRole.Label, InkRole.Primary);
        Name(hideSettings, "关闭设置");
        hideSettings.Click += (_, _) => Hide();
        Grid.SetColumn(hideSettings, 1); footer.Children.Add(hideSettings);
        WrapActions(footer, saveHint, hideSettings);
        Grid.SetRow(footer, 2);
        pane.Children.Add(footer);
        Grid.SetRow(_tabs, 0); root.Children.Add(_tabs);
        root.SizeChanged += (_, _) => UpdateWindowShape();
        pane.SizeChanged += (_, _) => UpdateWindowShape();
        _tabs.SizeChanged += (_, _) => UpdateWindowShape();
        ConfigureGlassControls(root);
        Content = root;
        ExtendsContentIntoTitleBar = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(false, false);
        SettingsWindowShape.ConfigureFrame(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Name(_blur, "玻璃强度"); Name(_transparency, "玻璃透明度");
        Name(_hover, "悬停展开延迟，毫秒"); Name(_leave, "离开收起延迟，毫秒");
        Name(_hotkey, "显示或隐藏快捷键，聚焦后按组合键绑定");
        Name(_diagnosticPath, "已导出的诊断文件路径");
        Name(_reducedMotion, "简化动画");
        Name(_fullscreen, "全屏时自动隐藏");
        Name(_startup, "登录 Windows 后启动");
        Name(_audio, "真实音频波浪条"); Name(_companion, "陪伴球"); Name(_liquidGlass, "液态玻璃");
        Name(_companionFollowMouse, "陪伴球跟随鼠标");
        Name(_clockTime, "显示时间"); Name(_clockDate, "显示日期"); Name(_clockWeekday, "显示星期");
    }

    void BindEvents()
    {
        _blur.ValueChanged += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { Blur = (float)_blur.Value }); };
        _transparency.ValueChanged += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { TintOpacity = (float)(1 - _transparency.Value / 100) }); };
        _hover.ValueChanged += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { HoverDelayMs = (int)_hover.Value }); };
        _leave.ValueChanged += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { LeaveDelayMs = (int)_leave.Value }); };
        _reducedMotion.Toggled += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { ReducedMotion = _reducedMotion.IsOn }); };
        _fullscreen.Toggled += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { AvoidFullscreen = _fullscreen.IsOn }); };
        _startup.Toggled += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { RunAtStartup = _startup.IsOn }); };
        _audio.Toggled += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { AudioVisualizationEnabled = _audio.IsOn }); };
        _companion.Toggled += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { CompanionEnabled = _companion.IsOn }); };
        _companionFollowMouse.Toggled += async (_, _) => { if (!_refreshing) { QueuePreferences(CurrentPreferences with { CompanionFollowMouse = _companionFollowMouse.IsOn }); await FlushPendingAsync(); } };
        _liquidGlass.Toggled += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { LiquidGlassEnabled = _liquidGlass.IsOn }); };
        _clockTime.Toggled += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { ClockShowTime = _clockTime.IsOn }); };
        _clockDate.Toggled += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { ClockShowDate = _clockDate.IsOn }); };
        _clockWeekday.Toggled += (_, _) => { if (!_refreshing) QueuePreferences(CurrentPreferences with { ClockShowWeekday = _clockWeekday.IsOn }); };
        _hotkey.KeyDown += CaptureHotkey;
        _colorPicker.ColorChanged += (_, args) =>
        {
            if (_refreshing) return;
            _hexDirty = false;
            SetCompanionAppearance(0xFF000000u | (uint)args.NewColor.R << 16 | (uint)args.NewColor.G << 8 | args.NewColor.B,
                CurrentPreferences.CompanionAppearanceId);
        };
        // Spectrum/slider children handle the routed release themselves.
        _colorPicker.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(async (_, _) => await FlushPendingAsync()), true);
        _companionForm.SelectionChanged += async (_, _) =>
        {
            if (_refreshing || _companionForm.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
            SetCompanionAppearance(CurrentPreferences.CompanionColor, id); await FlushPendingAsync();
        };
        _customColor.KeyDown += async (_, args) =>
        {
            if (args.Key != VirtualKey.Enter) return;
            args.Handled = true; if (CommitHexColor()) await FlushPendingAsync();
        };
        _customColor.TextChanged += (_, _) => { if (!_refreshing && !_writingHex) _hexDirty = true; };
        _customColor.LostFocus += async (_, _) => { if (!_refreshing && CommitHexColor()) await FlushPendingAsync(); };
        _visibility.Click += (_, _) => _coordinator.ToggleVisibility();
    }

    WidgetPreferences CurrentPreferences => _pending ?? _inFlight ?? _coordinator.Preferences;

    bool CommitHexColor()
    {
        if (!_hexDirty) return false;
        string hex = _customColor.Text.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
        {
            _colorFeedback.Text = "请输入六位 RGB，例如 #202329。"; _colorFeedback.Visibility = Visibility.Visible; return false;
        }
        _hexDirty = false; _writingHex = true;
        try { _customColor.Text = $"#{rgb:X6}"; }
        finally { _writingHex = false; }
        SetCompanionAppearance(0xFF000000 | rgb, CurrentPreferences.CompanionAppearanceId); return true;
    }

    void SetCompanionAppearance(uint color, string id)
    {
        color |= 0xFF000000; id = CompanionAppearanceIds.Normalize(id);
        _appearanceRevision++;
        if (!_hexDirty) _colorFeedback.Visibility = Visibility.Collapsed;
        // Live preview and persisted preferences have independent cadences. Pending always holds the latest valid value.
        _coordinator.PreviewCompanionAppearance(color, id);
        QueuePreferences(CurrentPreferences with { CompanionColor = color, CompanionAppearanceId = id });
    }

    void RefreshCompanionPreview(uint color, string appearanceId)
    {
        if (_previewAppearance == (color, appearanceId)) return;
        _previewAppearance = (color, appearanceId);
        var points = CompanionContour.GetPoints(appearanceId, CompanionShapePose.Rounded);
        var figure = new PathFigure { IsClosed = true, IsFilled = true,
            StartPoint = new Windows.Foundation.Point(12 + points[0].X * 3, 16 + points[0].Y * 3) };
        for (int index = 1; index < points.Count; index++)
            figure.Segments.Add(new LineSegment { Point = new(12 + points[index].X * 3, 16 + points[index].Y * 3) });
        var geometry = new PathGeometry(); geometry.Figures.Add(figure);
        // The sample represents the chosen RGB. Theme-role substitution would map
        // both the body and its eyes to WindowText in high contrast.
        _companionPreview.Data = geometry; _companionPreview.Fill = ColorBrush(color);
        _companionPreview.Stroke = ColorBrush(color == 0xFFFFFFFF ? 0xFFB9C1C8 : color);
        double r = ((color >> 16) & 255) / 255d, g = ((color >> 8) & 255) / 255d, b = (color & 255) / 255d;
        uint eye = .2126 * r * r + .7152 * g * g + .0722 * b * b > .20 ? 0xFF202329 : 0xFFFFFFFF;
        _previewLeftEye.Fill = _previewRightEye.Fill = ColorBrush(eye);
    }

    void QueuePreferences(WidgetPreferences preferences)
    {
        if (_disposed) return;
        _pending = preferences.Validated();
        _pendingAppearanceRevision = _appearanceRevision;
        Refresh();
        _changeTimer.Stop();
        _changeTimer.Start();
    }

    async Task FlushPreferencesAsync()
    {
        if (_disposed || _updating || _pending is null) return;
        var requested = _pending;
        long requestedAppearanceRevision = _pendingAppearanceRevision;
        _pending = null;
        _updating = true;
        _inFlight = requested;
        _updateCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // Position changes belong to the coordinator and may arrive while a slider request waits.
            var current = _coordinator.Preferences;
            requested = requested with { MonitorId = current.MonitorId, AnchorX = current.AnchorX, AnchorY = current.AnchorY };
            var result = await _coordinator.UpdatePreferencesAsync(requested);
            if (!result.Success)
            {
                if (_pending is not null) _pending = RebasePending(_pending, requested, _coordinator.Preferences,
                    _pendingAppearanceRevision > requestedAppearanceRevision);
                ShowFeedback(result.Message ?? "设置未能保存，请重试。", error: true);
            }
            else if (!string.IsNullOrWhiteSpace(result.Message)) ShowFeedback(result.Message);
        }
        catch (Exception)
        {
            if (_pending is not null) _pending = RebasePending(_pending, requested, _coordinator.Preferences,
                _pendingAppearanceRevision > requestedAppearanceRevision);
            ShowFeedback("设置未能保存，请重试。", error: true);
        }
        finally
        {
            _updating = false;
            _inFlight = null;
            _updateCompletion?.TrySetResult();
            if (!_disposed)
            {
                // A coordinator save can clear its temporary preview. Restore the
                // newer pending choice before the next presentation frame.
                if (_pending is { } pending && _pendingAppearanceRevision > requestedAppearanceRevision)
                    _coordinator.PreviewCompanionAppearance(pending.CompanionColor, pending.CompanionAppearanceId);
                Refresh();
                if (_pending is not null) _changeTimer.Start();
            }
        }
    }

    public async Task FlushPendingAsync()
    {
        _changeTimer.Stop();
        while (!_disposed)
        {
            if (_updating && _updateCompletion is { } completion) { await completion.Task; continue; }
            if (_pending is null) return;
            await FlushPreferencesAsync();
        }
    }

    static WidgetPreferences RebasePending(WidgetPreferences pending, WidgetPreferences requested, WidgetPreferences actual,
        bool preservePendingAppearance)
        => actual with
        {
            Blur = pending.Blur != requested.Blur ? pending.Blur : actual.Blur,
            TintOpacity = pending.TintOpacity != requested.TintOpacity ? pending.TintOpacity : actual.TintOpacity,
            ReducedMotion = pending.ReducedMotion != requested.ReducedMotion ? pending.ReducedMotion : actual.ReducedMotion,
            ClockShowTime = pending.ClockShowTime != requested.ClockShowTime ? pending.ClockShowTime : actual.ClockShowTime,
            ClockShowDate = pending.ClockShowDate != requested.ClockShowDate ? pending.ClockShowDate : actual.ClockShowDate,
            ClockShowWeekday = pending.ClockShowWeekday != requested.ClockShowWeekday ? pending.ClockShowWeekday : actual.ClockShowWeekday,
            AudioVisualizationEnabled = pending.AudioVisualizationEnabled != requested.AudioVisualizationEnabled ? pending.AudioVisualizationEnabled : actual.AudioVisualizationEnabled,
            CompanionEnabled = pending.CompanionEnabled != requested.CompanionEnabled ? pending.CompanionEnabled : actual.CompanionEnabled,
            CompanionFollowMouse = pending.CompanionFollowMouse != requested.CompanionFollowMouse ? pending.CompanionFollowMouse : actual.CompanionFollowMouse,
            CompanionColor = preservePendingAppearance || pending.CompanionColor != requested.CompanionColor ? pending.CompanionColor : actual.CompanionColor,
            CompanionAppearanceId = preservePendingAppearance || pending.CompanionAppearanceId != requested.CompanionAppearanceId ? pending.CompanionAppearanceId : actual.CompanionAppearanceId,
            LiquidGlassEnabled = pending.LiquidGlassEnabled != requested.LiquidGlassEnabled ? pending.LiquidGlassEnabled : actual.LiquidGlassEnabled,
            HoverDelayMs = pending.HoverDelayMs != requested.HoverDelayMs ? pending.HoverDelayMs : actual.HoverDelayMs,
            LeaveDelayMs = pending.LeaveDelayMs != requested.LeaveDelayMs ? pending.LeaveDelayMs : actual.LeaveDelayMs,
            AvoidFullscreen = pending.AvoidFullscreen != requested.AvoidFullscreen ? pending.AvoidFullscreen : actual.AvoidFullscreen,
            RunAtStartup = pending.RunAtStartup != requested.RunAtStartup ? pending.RunAtStartup : actual.RunAtStartup,
            HotkeyKey = pending.HotkeyKey != requested.HotkeyKey ? pending.HotkeyKey : actual.HotkeyKey,
            HotkeyModifiers = pending.HotkeyModifiers != requested.HotkeyModifiers ? pending.HotkeyModifiers : actual.HotkeyModifiers,
            OnboardingSeen = pending.OnboardingSeen != requested.OnboardingSeen ? pending.OnboardingSeen : actual.OnboardingSeen
        };

    void CaptureHotkey(object sender, KeyRoutedEventArgs args)
    {
        if (_coordinator.IsUiPreview) return;
        if (args.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
            VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.Menu or
            VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.LeftWindows or VirtualKey.RightWindows) return;
        if (args.Key == VirtualKey.Tab) return;
        args.Handled = true;
        if (args.Key == VirtualKey.Escape) { _hotkeyHint.Text = "点击输入框后按组合键，例如 Ctrl + Shift + G。"; return; }
        uint modifiers = (Down(VirtualKey.Menu) ? 1u : 0u) | (Down(VirtualKey.Control) ? 2u : 0u) |
            (Down(VirtualKey.Shift) ? 4u : 0u) | (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows) ? 8u : 0u);
        if ((modifiers & 11) == 0)
        {
            _hotkeyHint.Text = "请包含 Ctrl、Alt 或 Windows 键，再配合字母、数字或功能键。";
            return;
        }
        _hotkeyHint.Text = "若组合键被其他程序占用，仍保留原绑定。";
        QueuePreferences(CurrentPreferences with { HotkeyModifiers = modifiers, HotkeyKey = (uint)args.Key });
    }

    void Refresh()
    {
        if (_disposed || _minimalContent) return;
        _refreshing = true;
        try
        {
            var preference = CurrentPreferences;
            _blur.Value = preference.Blur;
            _transparency.Value = Math.Round((1 - preference.TintOpacity) * 100);
            _hover.Value = preference.HoverDelayMs; _leave.Value = preference.LeaveDelayMs;
            _reducedMotion.IsOn = preference.ReducedMotion;
            _clockTime.IsOn = preference.ClockShowTime; _clockDate.IsOn = preference.ClockShowDate; _clockWeekday.IsOn = preference.ClockShowWeekday;
            _acrylicBackdrop?.SetTintOpacity(preference.TintOpacity);
            _audio.IsOn = preference.AudioVisualizationEnabled;
            _companion.IsOn = preference.CompanionEnabled;
            _companionFollowMouse.IsOn = preference.CompanionFollowMouse;
            _liquidGlass.IsOn = preference.LiquidGlassEnabled;
            RefreshRuntimeStatus();
            if (_customColor.FocusState == FocusState.Unfocused && !_hexDirty)
            {
                _customColor.Text = $"#{preference.CompanionColor & 0x00FFFFFF:X6}";
                _hexDirty = false;
            }
            var companionColor = Color.FromArgb(255, (byte)(preference.CompanionColor >> 16), (byte)(preference.CompanionColor >> 8), (byte)preference.CompanionColor);
            if (_colorPicker.Color != companionColor) _colorPicker.Color = companionColor;
            _companionForm.SelectedIndex = preference.CompanionAppearanceId == CompanionAppearanceIds.CatEar ? 1 : preference.CompanionAppearanceId == CompanionAppearanceIds.Devil ? 2 : 0;
            RefreshCompanionPreview(preference.CompanionColor, preference.CompanionAppearanceId);
            _fullscreen.IsOn = preference.AvoidFullscreen; _startup.IsOn = preference.RunAtStartup;
            _blurLabel.Text = $"{preference.Blur:0} px";
            _transparencyLabel.Text = $"{(1 - preference.TintOpacity) * 100:0}%";
            _hoverLabel.Text = $"{preference.HoverDelayMs / 1000d:0.##} 秒";
            _leaveLabel.Text = $"{preference.LeaveDelayMs / 1000d:0.##} 秒";
            _hotkey.Text = FormatHotkey(preference.HotkeyModifiers, preference.HotkeyKey);
            _onboarding.Visibility = preference.OnboardingSeen ? Visibility.Collapsed : Visibility.Visible;
            string visibilityAction = _coordinator.IsWidgetVisible ? "隐藏组件" : "显示组件";
            _visibilityText.Text = visibilityAction;
            Name(_visibility, visibilityAction);
            ToolTipService.SetToolTip(_visibility, visibilityAction);
            var media = _coordinator.Media;
            _connection.Text = media.Connected ? "已连接网易云音乐" : "等待音乐播放";
            var controls = new List<string>();
            if (media.Capabilities.Previous) controls.Add("上一首");
            if (media.Capabilities.Toggle) controls.Add("播放 / 暂停");
            if (media.Capabilities.Next) controls.Add("下一首");
            if (media.Capabilities.Seek && media.Timeline?.Valid == true) controls.Add("进度跳转");
            _capabilities.Text = media.Connected
                ? (controls.Count == 0 ? "当前音乐来源暂不支持播放控制。" : "支持" + string.Join("、", controls) + "。") +
                    (media.Timeline?.Valid != true ? "播放时间暂不可用。" : "")
                : "在网易云音乐中播放歌曲，组件会自动连接。";
            _status.Text = _coordinator.ConnectionIssue ?? "";
            _status.Visibility = string.IsNullOrWhiteSpace(_status.Text) ? Visibility.Collapsed : Visibility.Visible;
            _connectionHelp.Visibility = media.Connected && string.IsNullOrWhiteSpace(_status.Text) ? Visibility.Collapsed : Visibility.Visible;
            RefreshPlugins();
        }
        finally { _refreshing = false; }
    }

    public static string FormatHotkey(uint modifiers, uint key)
    {
        if (key == 0) return "未绑定";
        var pieces = new List<string>();
        if ((modifiers & 2) != 0) pieces.Add("Ctrl");
        if ((modifiers & 1) != 0) pieces.Add("Alt");
        if ((modifiers & 4) != 0) pieces.Add("Shift");
        if ((modifiers & 8) != 0) pieces.Add("Win");
        pieces.Add(key switch
        {
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
            >= 0x70 and <= 0x87 => $"F{key - 0x6F}",
            0x20 => "Space", 0x0D => "Enter", 0x1B => "Esc",
            _ => ((VirtualKey)key).ToString()
        });
        return string.Join(" + ", pieces);
    }

    void ReportResult(SettingsUpdateResult result)
    {
        _pluginsKey = null;
        ShowFeedback(result.Message ?? (result.Success ? "已更新。" : "操作未完成，请重试。"), !result.Success);
        Refresh();
    }

    void RefreshRuntimeStatus()
    {
        if (_disposed || _minimalContent) return;
        _tabs?.RefreshAccessibility();
        // Native capture advances independently of preference/scene Changed events.
        // Read its live state, fallback reason and measured counters verbatim. Updating
        // these two labels leaves plugin rows, controls and page selection intact.
        try
        {
            string liquid = _coordinator.LiquidGlassStatus;
            if (_liquidGlassStatus.Text != liquid) _liquidGlassStatus.Text = liquid;
        }
        catch
        {
            const string unavailable = "材质状态暂不可用，请稍后重试。";
            if (_liquidGlassStatus.Text != unavailable) _liquidGlassStatus.Text = unavailable;
        }
        string audio = _coordinator.AudioStatus;
        if (_audioStatus.Text != audio) _audioStatus.Text = audio;
    }

    void RefreshPlugins()
    {
        var statuses = _coordinator.Plugins;
        var pages = _coordinator.PluginPages;
        string key = JsonSerializer.Serialize(new { statuses, pages });
        if (_pluginsKey == key) return;
        _pluginsKey = key;
        _pluginList.Children.Clear(); _pluginPages.Children.Clear();
        foreach (var status in statuses)
        {
            var panel = new StackPanel { Spacing = 8, Padding = new Thickness(16) };
            var heading = new Grid { ColumnSpacing = 16 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var labels = new StackPanel { Spacing = 4 };
            labels.Children.Add(SettingTitle(status.Name));
            labels.Children.Add(Description($"{status.Version} · {PluginStateLabel(status.State)}"));
            heading.Children.Add(labels);
            if (!status.BuiltIn)
            {
                var enabled = new ToggleSwitch { IsOn = status.Enabled, OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
                Name(enabled, $"启用 {status.Name}");
                enabled.Toggled += async (_, _) =>
                {
                    enabled.IsEnabled = false;
                    try { await FlushPendingAsync(); ReportResult(await _coordinator.SetPluginEnabledAsync(status.Id, enabled.IsOn)); }
                    catch { ShowFeedback("插件状态更新失败。", error: true); }
                    finally { enabled.IsEnabled = true; }
                };
                Grid.SetColumn(enabled, 1); heading.Children.Add(enabled); WrapActions(heading, labels, enabled);
            }
            panel.Children.Add(heading);
            panel.Children.Add(Description(status.ResourceDescription));
            if (!string.IsNullOrWhiteSpace(status.Reason)) panel.Children.Add(Description(status.Reason));
            foreach (var setting in _coordinator.GetPluginSettings(status.Id))
                panel.Children.Add(PluginSettingRow(status.Id, setting));
            if (!status.BuiltIn)
            {
                var remove = CommandButton("移除插件", "\uE74D");
                remove.Click += async (_, _) =>
                {
                    remove.IsEnabled = false;
                    try { await FlushPendingAsync(); ReportResult(await _coordinator.RemovePluginAsync(status.Id)); }
                    catch { ShowFeedback("插件移除失败。", error: true); }
                    finally { remove.IsEnabled = true; }
                };
                panel.Children.Add(remove);
            }
            _pluginList.Children.Add(Surface(panel));
        }
        if (pages.Count > 0) _pluginPages.Children.Add(SettingTitle("插件工具"));
        foreach (var page in pages)
        {
            var panel = new StackPanel { Spacing = 12, Padding = new Thickness(16) };
            panel.Children.Add(SettingTitle(page.Name));
            foreach (var item in page.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.CommandId))
                {
                    var command = CommandButton(item.Label, "\uE768");
                    command.Click += async (_, _) =>
                    {
                        int slash = item.CommandId.IndexOf('/');
                        if (slash <= 0 || slash == item.CommandId.Length - 1) { ShowFeedback("插件操作标识无效。", error: true); return; }
                        command.IsEnabled = false;
                        try { ReportResult(await _coordinator.ExecutePluginCommandAsync(item.CommandId[..slash], item.CommandId[(slash + 1)..])); }
                        catch { ShowFeedback("插件操作未能完成。", error: true); }
                        finally { command.IsEnabled = true; }
                    };
                    panel.Children.Add(command);
                    if (!string.IsNullOrWhiteSpace(item.Value)) panel.Children.Add(Description(item.Value));
                }
                else
                {
                    var text = new StackPanel { Spacing = 4 };
                    text.Children.Add(Label(item.Label));
                    if (!string.IsNullOrWhiteSpace(item.Value)) text.Children.Add(Description(item.Value));
                    panel.Children.Add(text);
                }
            }
            _pluginPages.Children.Add(Surface(panel));
        }
    }

    FrameworkElement PluginSettingRow(string pluginId, PluginSetting setting)
    {
        var configuration = _coordinator.GetPluginConfiguration(pluginId);
        string value = configuration is { ValueKind: JsonValueKind.Object } json && json.TryGetProperty(setting.Id, out var found)
            ? found.ToString() : setting.DefaultValue;
        if (setting.Kind == PluginSettingKind.Boolean)
        {
            var toggle = new ToggleSwitch { IsOn = bool.TryParse(value, out var on) && on, OnContent = "", OffContent = "", MinWidth = 0, Width = 52 };
            Name(toggle, setting.Name);
            toggle.Toggled += async (_, _) => await SavePluginValueAsync(pluginId, setting.Id, JsonSerializer.SerializeToElement(toggle.IsOn));
            return ToggleRow(setting.Name, toggle, setting.Description);
        }
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(SettingTitle(setting.Name));
        if (!string.IsNullOrWhiteSpace(setting.Description)) panel.Children.Add(Description(setting.Description));
        var input = new TextBox { Text = value, MinWidth = 0, MinHeight = 40, CornerRadius = new CornerRadius(24),
            Background = Brush(ButtonColor), BorderBrush = Brush(BorderColor), HorizontalAlignment = HorizontalAlignment.Stretch };
        SettingsTypography.Apply(input, TextRole.Body, InkRole.Secondary);
        Name(input, setting.Name);
        var save = CommandButton("保存", "\uE74E");
        save.Click += async (_, _) =>
        {
            JsonElement next;
            if (setting.Kind == PluginSettingKind.Number)
            {
                if (!double.TryParse(input.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number))
                { ShowFeedback("请输入有效数字。", error: true); return; }
                next = JsonSerializer.SerializeToElement(number);
            }
            else next = JsonSerializer.SerializeToElement(input.Text);
            save.IsEnabled = false;
            try { await SavePluginValueAsync(pluginId, setting.Id, next); }
            finally { save.IsEnabled = true; }
        };
        var controls = new Grid { ColumnSpacing = 8 };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.Children.Add(input); Grid.SetColumn(save, 1); controls.Children.Add(save); WrapActions(controls, input, save);
        panel.Children.Add(controls); return panel;
    }

    async Task SavePluginValueAsync(string pluginId, string settingId, JsonElement value)
    {
        try
        {
            var current = _coordinator.GetPluginConfiguration(pluginId);
            var properties = current is { ValueKind: JsonValueKind.Object }
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(current.Value.GetRawText()) ?? new()
                : new Dictionary<string, JsonElement>();
            properties[settingId] = value;
            ReportResult(await _coordinator.SavePluginConfigurationAsync(pluginId, JsonSerializer.SerializeToElement(properties)));
        }
        catch { ShowFeedback("插件设置未能保存。", error: true); }
    }

    static string PluginStateLabel(PluginRunState state) => state switch
    {
        PluginRunState.Running => "运行中", PluginRunState.Paused => "已暂停", PluginRunState.Disabled => "已关闭",
        PluginRunState.Starting => "启动中", PluginRunState.Faulted => "已回退", PluginRunState.RestartRequired => "需要重启", _ => "已停止"
    };

    /// <summary>Star text/Auto action rows become two rows before their controls can exceed the viewport.</summary>
    static void WrapActions(Grid row, FrameworkElement first, FrameworkElement action)
    {
        row.RowSpacing = 8;
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Arrange(double width)
        {
            bool narrow = width < 280 * Math.Max(1, SystemColors.TextScaleFactor);
            Grid.SetRow(action, narrow ? 1 : 0); Grid.SetColumn(action, narrow ? 0 : 1);
            Grid.SetColumnSpan(first, narrow ? 2 : 1);
            action.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        }
        row.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        row.Loaded += (_, _) => Arrange(row.ActualWidth);
    }

    void ConfigureGlassControls(Grid root)
    {
        // Keep standard WinUI focus/input behavior; only material tokens change.
        if (!HighContrast.HighContrast)
        {
            foreach (string key in new[] { "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ToggleSwitchFillOff", "ToggleSwitchFillOffPointerOver", "ToggleSwitchFillOffPressed" })
                root.Resources[key] = Brush(ButtonColor);
            foreach (string key in new[] { "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed", "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed" })
                root.Resources[key] = Brush(0xE6FFFFFF);
            foreach (string key in new[] { "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed", "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed" })
                root.Resources[key] = Brush(0xFF1689F9);
        }
        foreach (var toggle in new[] { _reducedMotion, _fullscreen, _startup, _audio, _companion, _companionFollowMouse, _liquidGlass, _clockTime, _clockDate, _clockWeekday }) toggle.MinHeight = 44;
        foreach (var input in new[] { _hotkey, _customColor, _diagnosticPath })
        {
            SettingsTypography.Apply(input, input == _diagnosticPath ? TextRole.CaptionSettings : TextRole.Body, InkRole.Secondary);
            input.MinHeight = 40; input.CornerRadius = new CornerRadius(24);
            input.Background = Brush(ButtonColor); input.BorderBrush = Brush(BorderColor);
            input.Padding = new Thickness(14, 8, 14, 8);
        }
        SettingsTypography.Apply(_companionForm, TextRole.Label, InkRole.Primary);
        SettingsTypography.ApplyInfoBar(_feedback);
    }

    void ShowFeedback(string message, bool error = false)
    {
        if (_disposed) return;
        _feedback.Title = error ? "暂未完成" : "已更新";
        _feedback.Message = message;
        _feedback.Severity = error ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        _feedback.IsOpen = true;
    }

    void ResizeToWorkArea()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = Math.Max(96u, GetDpiForWindow(hwnd)) / 96d;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int width = Math.Min((int)Math.Round(880 * scale), Math.Max(1, area.Width - 48));
        int height = Math.Min((int)Math.Round(680 * scale), Math.Max(1, area.Height - 64));
        AppWindow.Resize(new(width, height));
        AppWindow.Move(new(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2));
    }

    static StackPanel Section(string title, string description)
    {
        var panel = new StackPanel { Spacing = 16 };
        var heading = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 2) };
        var titleText = SettingsTypography.Apply(new TextBlock { Text = title }, TextRole.PageTitle, InkRole.Primary);
        var descriptionText = Description(description);
        heading.Children.Add(titleText);
        heading.Children.Add(descriptionText);
        panel.Children.Add(heading);
        return panel;
    }

    static Border Group(params FrameworkElement[] rows)
    {
        var panel = new StackPanel();
        for (int index = 0; index < rows.Length; index++)
        {
            if (index > 0) panel.Children.Add(new Border { Height = 1, Background = Brush(BorderColor), Margin = new Thickness(16, 0, 16, 0) });
            panel.Children.Add(rows[index]);
        }
        return Surface(panel);
    }

    static Border Surface(UIElement child) => new()
    {
        Child = child, Background = Brush(SurfaceColor), BorderBrush = Brush(BorderColor),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24)
    };

    static StackPanel SliderRow(string title, TextBlock value, Slider slider, string? description = null)
    {
        var panel = new StackPanel { Spacing = 6, Padding = new Thickness(16, 12, 16, 10) };
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = SettingTitle(title);
        heading.Children.Add(titleText);
        SettingsTypography.Apply(value, TextRole.Body, InkRole.Secondary, TextFlags.TabularDigitsOnly);
        value.MinWidth = 64;
        value.TextAlignment = TextAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 1); heading.Children.Add(value);
        panel.Children.Add(heading);
        if (description is not null) panel.Children.Add(Description(description));
        slider.MinHeight = 28;
        slider.IsThumbToolTipEnabled = false;
        panel.Children.Add(slider);
        return panel;
    }

    static Grid ToggleRow(string title, ToggleSwitch toggle, string? description = null)
    {
        var row = new Grid { ColumnSpacing = 16, MinHeight = 66, Padding = new Thickness(16, 12, 12, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(SettingTitle(title));
        if (description is not null) labels.Children.Add(Description(description));
        row.Children.Add(labels);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(toggle, 1); row.Children.Add(toggle);
        WrapActions(row, labels, toggle);
        return row;
    }

    static Button CommandButton(string title, string glyph)
    {
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        var label = SettingsTypography.Apply(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center }, TextRole.Label, InkRole.Primary);
        Grid.SetColumn(label, 1); content.Children.Add(label);
        var button = new Button
        {
            Content = content, CornerRadius = new CornerRadius(24), MinHeight = 40,
            Background = Brush(ButtonColor), BorderBrush = Brush(BorderColor), Foreground = Brush(InkColor),
            Padding = new Thickness(16, 8, 16, 8), HorizontalAlignment = HorizontalAlignment.Left
        };
        Name(button, title);
        ToolTipService.SetToolTip(button, title);
        return button;
    }

    static Border Separator() => new() { Height = 1, Background = Brush(BorderColor), Margin = new Thickness(0, 2, 0, 2) };
    static TextBlock Label(string text = "") => SettingsTypography.Apply(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, TextRole.Body, InkRole.Secondary);
    static TextBlock SettingTitle(string text) => SettingsTypography.Apply(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, TextRole.Label, InkRole.Primary);
    // Ink 2 is the conservative readable caption tier until the final composed
    // acrylic/background contrast matrix establishes that Ink 3 is safe.
    static TextBlock Description(string text = "") => SettingsTypography.Apply(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, TextRole.CaptionSettings, InkRole.Secondary);
    static SolidColorBrush Brush(uint argb)
    {
        if (HighContrast.HighContrast)
        {
            var role = argb switch
            {
                CanvasColor or SurfaceColor or ButtonColor or 0xFFF0F2F5 or 0xFFEFF6F4 or 0xFFF6F8FB => UIElementType.Window,
                0xFF28786E => UIElementType.Highlight,
                _ => UIElementType.WindowText
            };
            return new(SystemColors.UIElementColor(role));
        }
        return ColorBrush(argb);
    }
    static SolidColorBrush ColorBrush(uint argb) => new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    static void Name(DependencyObject control, string name) => AutomationProperties.SetName(control, name);
    static bool Down(VirtualKey key) => (GetKeyState((int)key) & 0x8000) != 0;
    [DllImport("user32.dll")] static extern short GetKeyState(int key);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint hwnd);

    public void CloseForShutdown()
    {
        if (_disposed) return;
        _shutdown = true;
        _changeTimer.Stop();
        _shown = false;
        _statusTimer.Stop();
        Close();
        Detach();
    }

    void Detach()
    {
        if (_disposed) return;
        _disposed = true;
        _shown = false;
        _tabs?.Dispose();
        _tabs = null;
        if (_shapeXamlRoot is not null) _shapeXamlRoot.Changed -= OnSettingsXamlRootChanged;
        _shapeXamlRoot = null;
        _windowShape?.Dispose(); _windowShape = null;
        _changeTimer.Stop();
        _changeTimer.Tick -= OnChangeTimerTick;
        _statusTimer.Stop();
        _statusTimer.Tick -= OnStatusTimerTick;
        _coordinator.Changed -= Refresh;
        AppWindow.Closing -= OnAppWindowClosing;
        Closed -= OnWindowClosed;
        DetachContentTrace();
    }

    public void Dispose() => CloseForShutdown();
}
