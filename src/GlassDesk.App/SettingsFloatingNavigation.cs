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
    int _pressedIndex;
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
        Height = 56; MaxWidth = 480; CornerRadius = new CornerRadius(28);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = Brush(0x52FFFFFF);
        BorderBrush = Brush(0x8AFFFFFF); BorderThickness = new Thickness(1);
        _selection = new Border
        {
            Height = 46, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(23), Background = Brush(0xD9FFFFFF), BorderBrush = Brush(0xCCFFFFFF),
            BorderThickness = new Thickness(1), RenderTransform = _translation, IsHitTestVisible = false
        };
        Children.Add(_selection);
        // The parent owns pointer capture for a continuous drag. Standard Buttons
        // capture the press before a bubbled parent handler can take ownership.
        // Buttons remain keyboard-focusable and expose their Invoke automation.
        var buttonGrid = new Grid { IsHitTestVisible = false };
        _buttons = new Button[titles.Length];
        for (int index = 0; index < titles.Length; index++)
        {
            int selected = index;
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var button = new Button
            {
                Content = titles[index], MinWidth = 0, MinHeight = 44, Background = null,
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(24),
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(2, 0, 2, 0), Foreground = SettingsTypography.Ink(InkRole.Primary)
            };
            AutomationProperties.SetName(button, titles[index]);
            button.Resources["ButtonBackgroundPointerOver"] = Brush(0x18FFFFFF);
            button.Resources["ButtonBackgroundPressed"] = Brush(0x28FFFFFF);
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
        _motion = DispatcherQueue.CreateTimer(); _motion.Interval = TimeSpan.FromMilliseconds(16);
        _motion.Tick += OnFrame;
        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPressed), true);
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnMoved), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnReleased), true);
        PointerCaptureLost += OnCaptureLost;
        PointerExited += (_, _) =>
        {
            if (_dragging || _pointerId is null) return;
            _pointerId = null; Select(SelectedIndex);
        };
        SizeChanged += (_, _) => ResetForSize(); Loaded += (_, _) => ResetForSize();
        Unloaded += (_, _) => Pause();
        UpdateLabels();
    }

    public void Select(int index)
    {
        if (_disposed || index < 0 || index >= _buttons.Length) return;
        bool changed = SelectedIndex != index; SelectedIndex = index;
        _target = 4 + SlotWidth * index; UpdateLabels();
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
            AutomationProperties.SetHelpText(_buttons[index], index == SelectedIndex ? "当前页面；可拖动选择底板" : "切换页面");
        }
    }

    public void RefreshAccessibility()
    {
        bool highContrast = Accessibility.HighContrast;
        Color window = highContrast ? SystemColors.UIElementColor(UIElementType.Window) : Color.FromArgb(0x52, 255, 255, 255);
        Color ink = SettingsTypography.Ink(InkRole.Primary).Color;
        Color highlight = highContrast ? SystemColors.UIElementColor(UIElementType.Highlight) : Color.FromArgb(0xD9, 255, 255, 255);
        var palette = (highContrast, window, ink, highlight);
        if (_palette == palette) return;
        _palette = palette;
        Background = new SolidColorBrush(window);
        BorderBrush = highContrast ? new SolidColorBrush(ink) : Brush(0x8AFFFFFF);
        // An outline stays readable as the shared plate passes underneath labels
        // during a direct drag; foreground never depends on a different slot's fill.
        _selection.Background = new SolidColorBrush(highContrast ? window : highlight);
        _selection.BorderBrush = highContrast ? new SolidColorBrush(highlight) : Brush(0xCCFFFFFF);
        _selection.BorderThickness = new Thickness(highContrast ? 2 : 1);
        foreach (var button in _buttons)
        {
            button.Foreground = new SolidColorBrush(ink);
            if (button.Content is TextBlock label) label.Foreground = new SolidColorBrush(ink);
        }
    }

    void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        if (_disposed || _pointerId is not null) return;
        var point = args.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        _motion.Stop(); _pointerId = args.Pointer.PointerId; _dragging = false;
        _pressedIndex = Math.Clamp((int)(point.Position.X / SlotWidth), 0, _buttons.Length - 1);
        _dragStartX = _lastDragX = point.Position.X; _dragStartY = point.Position.Y; _dragStartPosition = _position;
        _lastDragTime = Stopwatch.GetTimestamp();
    }

    void OnMoved(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(this);
        if (_pointerId != args.Pointer.PointerId) return;
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
            var role = argb == 0xD9FFFFFF ? UIElementType.Highlight :
                argb is 0xFF27313B or 0x8AFFFFFF or 0xCCFFFFFF ? UIElementType.WindowText : UIElementType.Window;
            return new(SystemColors.UIElementColor(role));
        }
        return new(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
    }
}
