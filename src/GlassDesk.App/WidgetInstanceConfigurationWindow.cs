using System.Runtime.InteropServices;
using System.Text.Json;
using GlassDesk.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;
using VirtualKey = Windows.System.VirtualKey;
using WindowId = Microsoft.UI.WindowId;

namespace GlassDesk.App;

/// <summary>
/// An instance-local draft editor. The coordinator owns the lifetime and registers
/// Handle plus the exact popup handles reported by PopupOwnershipChanged. Nothing
/// here changes the music clock preferences or writes a configuration file.
/// </summary>
internal sealed class WidgetInstanceConfigurationWindow : Window, IDisposable
{
    static readonly AccessibilitySettings Accessibility = new();
    static readonly UISettings SystemColors = new();
    readonly WidgetInstance _instance;
    readonly Action<JsonElement> _save;
    readonly Action _closed;
    readonly Grid _root = new() { RequestedTheme = ElementTheme.Light };
    readonly Button _zoneButton = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _zoneText = Text("", TextRole.Body, InkRole.Primary);
    readonly ToggleSwitch _seconds = Toggle("显示秒");
    readonly ToggleSwitch _weekday = Toggle("显示星期");
    readonly RadioButtons _hourFormat = Choices("时间制式", "24 小时制", "12 小时制");
    readonly RadioButtons _dateFormat = Choices("年月格式", "长格式", "简短格式");
    readonly RadioButtons _weekdayFormat = Choices("星期格式", "完整星期", "简短星期");
    readonly InfoBar _feedback = new() { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Warning };
    ZonePickerWindow? _popup;
    string? _zoneId;
    bool _isClosed, _closing, _saving;

    /// <summary>
    /// Raised before a picker is shown (true), and once it closes (false).
    /// Subscribe before Show(). Register these exact HWNDs with the parent's
    /// outside-click policy; do not classify other windows by process ID.
    /// </summary>
    public event Action<nint, bool>? PopupOwnershipChanged;

    // Stable values also let the coordinator unregister this window from the
    // closed callback, after WinUI has already destroyed its native target.
    public WindowId WindowId { get; }
    public nint Handle { get; }
    public IReadOnlyList<nint> PopupWindowHandles => _popup is { IsClosed: false } popup
        ? Array.AsReadOnly(new[] { popup.Handle }) : Array.Empty<nint>();

    public WidgetInstanceConfigurationWindow(WidgetInstance instance, Action<JsonElement> save, Action closed)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(closed);
        _instance = instance with { Settings = WidgetBoardData.CloneSettings(instance.Settings) };
        _save = save;
        _closed = closed;
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId = AppWindow.Id;
        bool time = instance.ProviderId == BuiltInWidgetIds.Time;
        bool date = instance.ProviderId == BuiltInWidgetIds.Date;
        Title = time ? "时间组件配置" : date ? "日期组件配置" : "组件配置";

        if (time)
        {
            var options = BuiltInWidgetProviders.ReadTimeSettings(_instance.Settings);
            _zoneId = options.TimeZoneId;
            _hourFormat.SelectedIndex = options.Use24Hour ? 0 : 1;
            _seconds.IsOn = options.ShowSeconds;
        }
        else if (date)
        {
            var options = BuiltInWidgetProviders.ReadDateSettings(_instance.Settings);
            _zoneId = options.TimeZoneId;
            _weekday.IsOn = options.ShowWeekday;
            _dateFormat.SelectedIndex = options.DateFormat == "short" ? 1 : 0;
            _weekdayFormat.SelectedIndex = options.WeekdayFormat == "short" ? 1 : 0;
            _weekdayFormat.IsEnabled = options.ShowWeekday;
        }

        ConfigurePalette(_root);
        _root.Background = Surface(false);
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var body = new StackPanel { Spacing = 20, Padding = new Thickness(24, 20, 24, 20) };
        var heading = new StackPanel { Spacing = 6 };
        heading.Children.Add(Text(time ? "时间" : date ? "日期" : "组件", TextRole.PageTitle, InkRole.Primary));
        heading.Children.Add(Text("仅应用于这个组件。", TextRole.CaptionSettings, InkRole.Secondary));
        body.Children.Add(heading);
        SettingsTypography.ApplyInfoBar(_feedback);
        body.Children.Add(_feedback);

        if (time || date)
        {
            body.Children.Add(Field("时区", BuildZoneButton(), "使用 Windows 已安装的时区，默认跟随系统。"));
            if (time)
            {
                body.Children.Add(_hourFormat);
                body.Children.Add(Field("显示秒", _seconds, "关闭时按分钟更新；隐藏后停止刷新。"));
            }
            else
            {
                body.Children.Add(_dateFormat);
                body.Children.Add(Field("显示星期", _weekday));
                body.Children.Add(_weekdayFormat);
            }
        }
        else body.Children.Add(Text("这个组件没有内置配置。", TextRole.Body, InkRole.Secondary));

        var scroller = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled
        };
        _root.Children.Add(scroller);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(24, 12, 24, 20)
        };
        var cancel = Button("取消");
        cancel.Click += OnCancel;
        actions.Children.Add(cancel);
        if (time || date)
        {
            var commit = Button("保存");
            // Keep opaque primary ink on a light button. The protected blue is
            // a boundary, not a low-contrast foreground for the small label.
            if (!Accessibility.HighContrast)
            {
                commit.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0x16, 0x89, 0xF9));
                commit.BorderThickness = new Thickness(2);
            }
            commit.Click += OnSave;
            actions.Children.Add(commit);
        }
        Grid.SetRow(actions, 1);
        _root.Children.Add(actions);
        _root.KeyDown += OnKeyDown;
        _weekday.Toggled += OnWeekdayToggled;
        Content = _root;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        Closed += OnClosed;
    }

    public void Show()
    {
        if (_isClosed) return;
        FitToWorkArea();
        Activate();
    }

    public new void Close()
    {
        if (_isClosed || _closing) return;
        _closing = true;
        try
        {
            ClosePopup();
            base.Close();
        }
        finally { if (!_isClosed) _closing = false; }
    }

    public void Dispose() => Close();

    void OnClosed(object sender, WindowEventArgs args)
    {
        if (_isClosed) return;
        _isClosed = true;
        ClosePopup();
        _root.KeyDown -= OnKeyDown;
        _weekday.Toggled -= OnWeekdayToggled;
        _zoneButton.Click -= OnZoneClick;
        Closed -= OnClosed;
        _closed();
    }

    void OnCancel(object sender, RoutedEventArgs args) => Close();
    void OnWeekdayToggled(object sender, RoutedEventArgs args) => _weekdayFormat.IsEnabled = _weekday.IsOn;
    void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape) return;
        if (_popup is not null) ClosePopup();
        else Close();
        args.Handled = true;
    }

    void OnSave(object sender, RoutedEventArgs args)
    {
        if (_isClosed || _closing || _saving) return;
        _saving = true;
        try
        {
            JsonElement settings = _instance.ProviderId == BuiltInWidgetIds.Time
                ? BuiltInWidgetProviders.ToJson(new TimeWidgetSettings(_hourFormat.SelectedIndex != 1, _seconds.IsOn, _zoneId))
                : BuiltInWidgetProviders.ToJson(new DateWidgetSettings(_weekday.IsOn,
                    _dateFormat.SelectedIndex == 1 ? "short" : "long",
                    _weekdayFormat.SelectedIndex == 1 ? "short" : "long", _zoneId));
            _save(WidgetBoardData.CloneSettings(settings));
        }
        catch (Exception)
        {
            _feedback.Title = "暂未保存";
            _feedback.Message = "无法保存这个组件的配置，请重试。";
            _feedback.IsOpen = true;
            return;
        }
        finally { _saving = false; }
        Close();
    }

    FrameworkElement BuildZoneButton()
    {
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(_zoneText);
        var chevron = new FontIcon { Glyph = "\uE70D", FontSize = 12, Foreground = SettingsTypography.Ink(InkRole.Primary), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 1);
        content.Children.Add(chevron);
        _zoneButton.Content = content;
        _zoneButton.MinHeight = 44;
        _zoneButton.Padding = new Thickness(14, 10, 14, 10);
        _zoneButton.CornerRadius = new CornerRadius(UiRadius.Full(44));
        _zoneButton.Click += OnZoneClick;
        Name(_zoneButton, "选择时区");
        AutomationProperties.SetHelpText(_zoneButton, "打开 Windows 已安装的时区列表。");
        RefreshZoneLabel();
        return _zoneButton;
    }

    void RefreshZoneLabel()
    {
        _zoneText.Text = _zoneId is null ? "跟随系统时区" : BuiltInWidgetProviders.ResolveTimeZone(_zoneId).DisplayName;
        AutomationProperties.SetItemStatus(_zoneButton, _zoneText.Text);
    }

    void OnZoneClick(object sender, RoutedEventArgs args)
    {
        if (_isClosed) return;
        if (_popup is not null) { ClosePopup(); return; }
        var picker = new ZonePickerWindow(Handle, _zoneId, id =>
        {
            _zoneId = id;
            RefreshZoneLabel();
        }, OnPickerClosed);
        _popup = picker;
        try
        {
            PopupOwnershipChanged?.Invoke(picker.Handle, true);
            if (_isClosed || picker.IsClosed) return;
            var origin = new NativePoint();
            if (!ClientToScreen(Handle, ref origin))
                throw new InvalidOperationException("The configuration window is no longer available.");
            Point anchor = _zoneButton.TransformToVisual(_root).TransformPoint(new Point(0, _zoneButton.ActualHeight + 4));
            double scale = Scale(Handle);
            picker.Show(WindowId, origin.X + (int)Math.Round(anchor.X * scale), origin.Y + (int)Math.Round(anchor.Y * scale), scale);
        }
        catch
        {
            ClosePopup();
            throw;
        }
    }

    void OnPickerClosed(ZonePickerWindow picker)
    {
        if (_popup == picker) _popup = null;
        PopupOwnershipChanged?.Invoke(picker.Handle, false);
    }

    void ClosePopup()
    {
        var picker = _popup;
        if (picker is null) return;
        _popup = null;
        picker.Close();
    }

    void FitToWorkArea()
    {
        double scale = Scale(Handle);
        var area = DisplayArea.GetFromWindowId(WindowId, DisplayAreaFallback.Nearest).WorkArea;
        int width = Math.Min((int)Math.Round(440 * scale), Math.Max(1, area.Width - 32));
        int height = Math.Min((int)Math.Round(560 * scale), Math.Max(1, area.Height - 48));
        AppWindow.Resize(new(width, height));
        AppWindow.Move(new(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2));
    }

    static StackPanel Field(string title, FrameworkElement control, string? description = null)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Text(title, TextRole.Label, InkRole.Primary));
        if (description is not null) panel.Children.Add(Text(description, TextRole.CaptionSettings, InkRole.Secondary));
        panel.Children.Add(control);
        return panel;
    }

    static RadioButtons Choices(string title, string first, string second)
    {
        var control = SettingsTypography.Apply(new RadioButtons { Header = title, MaxColumns = 2 }, TextRole.Body, InkRole.Primary);
        control.Items.Add(first);
        control.Items.Add(second);
        Name(control, title);
        return control;
    }

    static ToggleSwitch Toggle(string title)
    {
        var control = SettingsTypography.Apply(new ToggleSwitch { OnContent = "开", OffContent = "关", MinHeight = 44 }, TextRole.Body, InkRole.Primary);
        Name(control, title);
        return control;
    }

    static Button Button(string title)
    {
        var control = new Button
        {
            Content = Text(title, TextRole.Label, InkRole.Primary), MinWidth = 88, MinHeight = 44,
            Padding = new Thickness(18, 8, 18, 8), CornerRadius = new CornerRadius(UiRadius.Full(44))
        };
        Name(control, title);
        return control;
    }

    static TextBlock Text(string text, TextRole role, InkRole ink) => SettingsTypography.Apply(new TextBlock
    { Text = text, TextWrapping = TextWrapping.Wrap }, role, ink);

    static SolidColorBrush Surface(bool elevated) => new(Accessibility.HighContrast
        ? SystemColors.UIElementColor(UIElementType.Window)
        : elevated ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 244, 245, 247));

    static void ConfigurePalette(FrameworkElement root)
    {
        if (Accessibility.HighContrast) return;
        var accent = new SolidColorBrush(Color.FromArgb(255, 0x16, 0x89, 0xF9));
        foreach (string key in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" })
            root.Resources[key] = accent.Color;
        foreach (string key in new[] { "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed", "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedFillPointerOver", "RadioButtonOuterEllipseCheckedFillPressed", "RadioButtonOuterEllipseCheckedStroke", "RadioButtonOuterEllipseCheckedStrokePointerOver", "RadioButtonOuterEllipseCheckedStrokePressed" })
            root.Resources[key] = accent;
    }

    static void Name(DependencyObject control, string text) => AutomationProperties.SetName(control, text);
    static double Scale(nint hwnd) => Math.Max(96u, GetDpiForWindow(hwnd)) / 96d;
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    /// <summary>A controlled popup gives outside-click routing an exact HWND, including keyboard use.</summary>
    sealed class ZonePickerWindow : Window
    {
        readonly Action<string?> _select;
        readonly Action<ZonePickerWindow> _closed;
        readonly Grid _root = new() { RequestedTheme = ElementTheme.Light };
        readonly ListView _zones = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
        public bool IsClosed { get; private set; }
        public nint Handle { get; }

        public ZonePickerWindow(nint owner, string? selected, Action<string?> select, Action<ZonePickerWindow> closed)
        {
            _select = select;
            _closed = closed;
            Title = "选择时区";
            Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowLongPtr(Handle, -8, owner); // GWLP_HWNDPARENT: exact config-window owner, never process-wide.
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }
            ConfigurePalette(_root);
            _root.Background = Surface(true);
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.Children.Add(new Border { Child = Text("选择时区", TextRole.SectionTitle, InkRole.Primary), Padding = new Thickness(16, 12, 16, 8) });
            Name(_zones, "Windows 时区");
            ScrollViewer.SetHorizontalScrollBarVisibility(_zones, ScrollBarVisibility.Disabled);
            AddZone("跟随系统时区", null, selected is null);
            foreach (var zone in BuiltInWidgetProviders.InstalledTimeZones())
                AddZone(zone.DisplayName, zone.Id, string.Equals(zone.Id, selected, StringComparison.OrdinalIgnoreCase));
            _zones.ItemClick += OnItemClick;
            _zones.KeyDown += OnListKeyDown;
            Grid.SetRow(_zones, 1);
            _root.Children.Add(_zones);
            var cancel = Button("取消");
            cancel.HorizontalAlignment = HorizontalAlignment.Right;
            cancel.Margin = new Thickness(12, 8, 12, 12);
            cancel.Click += (_, _) => Close();
            Grid.SetRow(cancel, 2);
            _root.Children.Add(cancel);
            _root.KeyDown += OnKeyDown;
            _root.Loaded += OnLoaded;
            Content = _root;
            Closed += OnClosed;
        }

        void AddZone(string label, string? id, bool selected)
        {
            var row = new ListViewItem { Content = Text(label, TextRole.Body, InkRole.Primary), Tag = id, Padding = new Thickness(12, 8, 12, 8), MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            Name(row, label);
            _zones.Items.Add(row);
            if (selected) _zones.SelectedItem = row;
        }

        public void Show(WindowId ownerId, int x, int y, double scale)
        {
            if (IsClosed) return;
            var area = DisplayArea.GetFromWindowId(ownerId, DisplayAreaFallback.Nearest).WorkArea;
            int width = Math.Min((int)Math.Round(408 * scale), Math.Max(1, area.Width - 16));
            int height = Math.Min((int)Math.Round(360 * scale), Math.Max(1, area.Height - 16));
            AppWindow.Resize(new(width, height));
            AppWindow.Move(new(Math.Clamp(x, area.X, area.X + area.Width - width), Math.Clamp(y, area.Y, area.Y + area.Height - height)));
            Activate();
        }

        void OnLoaded(object sender, RoutedEventArgs args)
        {
            _root.Loaded -= OnLoaded;
            _zones.Focus(FocusState.Programmatic);
            if (_zones.SelectedItem is not null) _zones.ScrollIntoView(_zones.SelectedItem);
        }

        void OnItemClick(object sender, ItemClickEventArgs args)
        {
            if (args.ClickedItem is not ListViewItem item) return;
            _select(item.Tag as string);
            Close();
        }

        void OnKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Escape) { args.Handled = true; Close(); }
        }

        void OnListKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key == VirtualKey.Enter && _zones.SelectedItem is ListViewItem item)
            { args.Handled = true; _select(item.Tag as string); Close(); }
        }

        void OnClosed(object sender, WindowEventArgs args)
        {
            if (IsClosed) return;
            IsClosed = true;
            _zones.ItemClick -= OnItemClick;
            _zones.KeyDown -= OnListKeyDown;
            _root.KeyDown -= OnKeyDown;
            _root.Loaded -= OnLoaded;
            Closed -= OnClosed;
            _closed(this);
        }
    }
}
