using System.Diagnostics;
using GlassDesk.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace GlassDesk.App;

/// <summary>Draggable shared pill. A finite, critically damped settle retains position and velocity.</summary>
internal sealed class SettingsFloatingNavigation : Grid, IDisposable
{
    const uint SelectionFill = 0x1F1689F9; // accent.fill at 12%
    const uint HoverFill = 0x1420252C;     // ink at 8% on the light ribbon
    const double SelectionHeight = 46;

    static readonly AccessibilitySettings Accessibility = new();
    static readonly UISettings SystemColors = new();
    readonly Border _selection;
    readonly TranslateTransform _translation = new();
    readonly Button[] _buttons;
    readonly DispatcherQueueTimer _motion;
    readonly Func<bool> _reducedMotion;
    double _position = 4, _target = 4, _velocity;
    double _dragStartX, _dragStartY, _dragStartPosition, _lastDragX;
    long _lastFrame, _lastDragTime;
    uint? _pointerId;
    int _pressedIndex, _hoverIndex = -1;
    bool _dragging, _disposed;
    (bool HighContrast, Color Window, Color Ink, Color Highlight)? _palette;
    public int SelectedIndex { get; private set; }
    public event Action<int>? SelectionChanged;
    public event Action<double>? ProgressChanged;
    double SlotWidth => Math.Max(1, ActualWidth / _buttons.Length);
    double LastPosition => Math.Max(4, ActualWidth - SlotWidth + 4);

    public SettingsFloatingNavigation(string[] titles, Func<bool> reducedMotion)
    {
        _reducedMotion = reducedMotion;
        Height = 56; MaxWidth = 480; CornerRadius = new CornerRadius(UiRadius.Shell);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = Brush(0x52FFFFFF);
        BorderBrush = Brush(0x8AFFFFFF); BorderThickness = new Thickness(1);
        _selection = new Border
        {
            Height = SelectionHeight, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(UiRadius.Shell - 4), Background = Brush(SelectionFill),
            RenderTransform = _translation, IsHitTestVisible = false
        };
        Children.Add(_selection);
        // The parent owns pointer capture for a continuous drag. Standard Buttons
        // capture the press before a bubbled parent handler can take ownership.
        // Buttons remain keyboard-focusable and expose their Invoke automation;
        // the pointer-over visual is computed by the parent instead of hit testing.
        var buttonGrid = new Grid { IsHitTestVisible = false };
        _buttons = new Button[titles.Length];
        for (int index = 0; index < titles.Length; index++)
        {
            int selected = index;
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var button = new Button
            {
                Content = titles[index], MinWidth = 0, MinHeight = 44, Background = null,
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(UiRadius.Shell - 4),
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(2, 0, 2, 0), Foreground = SettingsTypography.Ink(InkRole.Primary)
            };
            AutomationProperties.SetName(button, titles[index]);
            button.Resources["ButtonBackgroundPointerOver"] = Brush(HoverFill);
            button.Resources["ButtonBackgroundPressed"] = Brush(0x1F20252C);
            button.Click += (_, _) => { if (_pointerId is null) Select(selected); };
            button.KeyDown += (_, args) =>
            {
                if (args.Key is not (Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End)) return;
                int next = args.Key switch
                {
                    Windows.System.VirtualKey.Home => 0, Windows.System.VirtualKey.End => titles.Length - 1,
                    Windows.System.VirtualKey.Left => (SelectedIndex + titles.Length - 1) % titles.Length,
                    _ => (SelectedIndex + 1) % titles.Length
                };
                Select(next); _buttons[next].Focus(FocusState.Keyboard); args.Handled = true;
            };
            Grid.SetColumn(button, index); buttonGrid.Children.Add(button); _buttons[index] = button;
        }
        Children.Add(buttonGrid);
        ConfigureFocusVisuals();
        _motion = DispatcherQueue.CreateTimer(); _motion.Interval = TimeSpan.FromMilliseconds(16);
        _motion.Tick += OnFrame;
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        PointerEntered += (_, args) =>
        {
            if (_pointerId is null) SetHover(SlotIndex(args.GetCurrentPoint(this).Position.X));
        };
        PointerCaptureLost += OnCaptureLost;
        PointerExited += (_, _) =>
        {
            SetHover(-1);
            if (_dragging || _pointerId is null) return;
            _pointerId = null; Select(SelectedIndex);
        };
        SizeChanged += (_, _) => ResetForSize(); Loaded += (_, _) => ResetForSize();
        Unloaded += (_, _) => Pause();
        UpdateLabels();
    }

    /// <summary>Keyboard-only focus ring: 2px accent.fill with a 1px light separator.</summary>
    void ConfigureFocusVisuals()
    {
        if (Accessibility.HighContrast) return;
        Resources["FocusVisualPrimaryBrush"] = Brush(0xFF1689F9);
        Resources["FocusVisualSecondaryBrush"] = Brush(0xFFFFFFFF);
        Resources["FocusVisualPrimaryThickness"] = new Thickness(2);
        Resources["FocusVisualSecondaryThickness"] = new Thickness(1);
    }

    public void Select(int index)
    {
        if (_disposed || index < 0 || index >= _buttons.Length) return;
        bool changed = SelectedIndex != index; SelectedIndex = index;
        _target = 4 + SlotWidth * index; UpdateLabels(); RefreshHoverVisuals();
        if (changed) SelectionChanged?.Invoke(index);
        if (_reducedMotion()) { _motion.Stop(); _position = _target; _velocity = 0; Present(); }
        else StartMotion();
    }

    void ResetForSize()
    {
        _motion.Stop(); _velocity = 0;
        _selection.Width = Math.Max(0, SlotWidth - 8);
        _position = _target = 4 + SlotWidth * SelectedIndex; Present();
    }

    void UpdateLabels()
    {
        RefreshAccessibility();
        for (int index = 0; index < _buttons.Length; index++)
        {
            SettingsTypography.ApplyNavigation(_buttons[index], index == SelectedIndex);
            SetInk(_buttons[index], index == SelectedIndex);
            AutomationProperties.SetHelpText(_buttons[index], index == SelectedIndex ? "当前页面；可拖动选择底板" : "切换页面");
        }
    }

    /// <summary>Selected labels use accent.ink; the others keep the primary ink tier.</summary>
    void SetInk(Button button, bool selected)
    {
        var fore = selected && !Accessibility.HighContrast
            ? new SolidColorBrush(Color.FromArgb(255, 0x0B, 0x6B, 0xCB))
            : SettingsTypography.Ink(InkRole.Primary);
        button.Foreground = fore;
        if (button.Content is TextBlock label) label.Foreground = fore;
    }

    /// <summary>
    /// The buttons are hit-test invisible so the parent can own drag capture.
    /// The hovered slot therefore has to be painted by the parent as well.
    /// </summary>
    void SetHover(int index)
    {
        if (_disposed || index == _hoverIndex) return;
        _hoverIndex = index;
        RefreshHoverVisuals();
    }

    void RefreshHoverVisuals()
    {
        // High contrast relies on the system focus/highlight visuals instead of
        // painting a fill that could hide the label.
        bool paint = !Accessibility.HighContrast;
        for (int slot = 0; slot < _buttons.Length; slot++)
            _buttons[slot].Background = paint && slot == _hoverIndex && slot != SelectedIndex ? Brush(HoverFill) : null;
    }

    int SlotIndex(double x) => Math.Clamp((int)(x / SlotWidth), 0, _buttons.Length - 1);

    public void RefreshAccessibility()
    {
        bool highContrast = Accessibility.HighContrast;
        Color window = highContrast ? SystemColors.UIElementColor(UIElementType.Window) : Color.FromArgb(0x52, 255, 255, 255);
        Color ink = SettingsTypography.Ink(InkRole.Primary).Color;
        Color selection = highContrast ? SystemColors.UIElementColor(UIElementType.Window) : Color.FromArgb(0x1F, 0x16, 0x89, 0xF9);
        var palette = (highContrast, window, ink, selection);
        if (_palette == palette) return;
        _palette = palette;
        Background = new SolidColorBrush(window);
        BorderBrush = highContrast ? new SolidColorBrush(ink) : Brush(0x8AFFFFFF);
        // An outline stays readable as the shared plate passes underneath labels
        // during a direct drag; foreground never depends on a different slot's fill.
        _selection.Background = new SolidColorBrush(selection);
        _selection.BorderBrush = highContrast ? new SolidColorBrush(ink) : null;
        _selection.BorderThickness = new Thickness(highContrast ? 2 : 0);
        for (int index = 0; index < _buttons.Length; index++) SetInk(_buttons[index], index == SelectedIndex);
    }

    void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_disposed || _pointerId is not null) return;
        var point = args.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        SetHover(-1);
        _motion.Stop(); _pointerId = args.Pointer.PointerId; _dragging = false;
        _pressedIndex = SlotIndex(point.Position.X);
        _dragStartX = _lastDragX = point.Position.X; _dragStartY = point.Position.Y; _dragStartPosition = _position;
        _lastDragTime = Stopwatch.GetTimestamp();
    }

    void OnMoved(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this);
        if (_pointerId is null) { SetHover(SlotIndex(point.Position.X)); return; }
        if (_pointerId != args.Pointer.PointerId) return;
        SetHover(-1);
        double horizontal = Math.Abs(point.Position.X - _dragStartX), vertical = Math.Abs(point.Position.Y - _dragStartY);
        if (!_dragging)
        {
            if (vertical > 8 && horizontal <= vertical * 1.25) { _pointerId = null; Select(SelectedIndex); return; }
            if (horizontal <= 8 || horizontal <= vertical * 1.25) return;
            if (!CapturePointer(args.Pointer)) { _pointerId = null; return; }
            _dragging = true;
        }
        long now = Stopwatch.GetTimestamp();
        double elapsed = Math.Max(.001, (now - _lastDragTime) / (double)Stopwatch.Frequency);
        double instantaneous = Math.Clamp((point.Position.X - _lastDragX) / elapsed, -2400, 2400);
        _velocity = _velocity * .45 + instantaneous * .55;
        _position = Math.Clamp(_dragStartPosition + point.Position.X - _dragStartX, 4, LastPosition);
        _lastDragX = point.Position.X; _lastDragTime = now; Present(); args.Handled = true;
    }

    void OnReleased(object sender, PointerRoutedEventArgs args)
    {
        if (_pointerId != args.Pointer.PointerId) return;
        var point = args.GetCurrentPoint(this);
        // Consume the release position even when the platform coalesces moves.
        double distance = point.Position.X - _dragStartX;
        if (!_dragging && Math.Abs(distance) > 8 && Math.Abs(distance) > Math.Abs(point.Position.Y - _dragStartY) * 1.25)
            _dragging = true;
        if (_dragging)
        {
            _dragging = true;
            _position = Math.Clamp(_dragStartPosition + distance, 4, LastPosition);
            Present();
        }
        double age = (Stopwatch.GetTimestamp() - _lastDragTime) / (double)Stopwatch.Frequency;
        _velocity *= Math.Exp(-Math.Max(0, age - .016) * 18);
        int target = _dragging
            ? Math.Clamp((int)Math.Round((_position - 4 + _velocity * .06) / SlotWidth), 0, _buttons.Length - 1)
            : _pressedIndex;
        bool clickInside = point.Position.X >= 0 && point.Position.X < ActualWidth && point.Position.Y >= 0 && point.Position.Y < ActualHeight;
        _pointerId = null; ReleasePointerCapture(args.Pointer); Select(_dragging || clickInside ? target : SelectedIndex);
        if (_dragging || clickInside) _buttons[SelectedIndex].Focus(FocusState.Pointer);
        if (clickInside) SetHover(SlotIndex(point.Position.X));
        args.Handled = true;
    }

    void OnCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        if (_pointerId is null) return;
        _pointerId = null; Select(SelectedIndex);
    }

    void StartMotion()
    {
        _lastFrame = Stopwatch.GetTimestamp();
        if (Math.Abs(_position - _target) < .05 && Math.Abs(_velocity) < .5) { _position = _target; _velocity = 0; Present(); return; }
        _motion.Start();
    }

    void OnFrame(DispatcherQueueTimer sender, object args)
    {
        long now = Stopwatch.GetTimestamp();
        double delta = Math.Clamp((now - _lastFrame) / (double)Stopwatch.Frequency, .001, .04); _lastFrame = now;
        if (_reducedMotion()) { _position = _target; _velocity = 0; }
        else
        {
            const double omega = 28;
            double error = _position - _target, coefficient = _velocity + omega * error;
            double decay = Math.Exp(-omega * delta);
            _position = _target + (error + coefficient * delta) * decay;
            _velocity = (_velocity - omega * coefficient * delta) * decay;
            if (_position < 4 || _position > LastPosition) { _position = Math.Clamp(_position, 4, LastPosition); _velocity = 0; }
        }
        if (Math.Abs(_position - _target) < .05 && Math.Abs(_velocity) < .5)
        { _position = _target; _velocity = 0; sender.Stop(); }
        Present();
    }

    void Present()
    {
        _translation.X = _position;
        ProgressChanged?.Invoke(Math.Clamp((_position - 4) / SlotWidth, 0, _buttons.Length - 1));
    }

    public void Pause()
    {
        _pointerId = null; ReleasePointerCaptures(); _motion.Stop();
    }

    public void Resume()
    {
        if (!_disposed && Math.Abs(_position - _target) >= .05) StartMotion();
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Pause();
        _motion.Tick -= OnFrame; SelectionChanged = null; ProgressChanged = null;
    }

    static SolidColorBrush Brush(uint argb)
    {
        if (Accessibility.HighContrast)
        {
            var role = argb == SelectionFill ? UIElementType.Window :
                argb is 0x8AFFFFFF or 0x1420252C or 0x1F20252C ? UIElementType.WindowText : UIElementType.Window;
            return new(SystemColors.UIElementColor(role));
        }
        return new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    }
}
