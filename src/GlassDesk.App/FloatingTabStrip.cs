using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GlassDesk.App;

/// <summary>Five equal, keyboard-reachable tabs share one moving selection surface.</summary>
internal sealed class FloatingTabStrip : Grid
{
    readonly Border _selection;
    readonly TranslateTransform _translation = new();
    readonly Button[] _buttons;
    readonly Func<bool> _reducedMotion;
    Storyboard? _motion;
    public int SelectedIndex { get; private set; }
    public event Action<int>? SelectionChanged;

    public FloatingTabStrip(string[] titles, Func<bool> reducedMotion)
    {
        _reducedMotion = reducedMotion;
        Height = 48; MaxWidth = 480;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(170, 232, 234, 237));
        CornerRadius = new CornerRadius(24);
        _selection = new Border
        {
            Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(20), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 32, 37, 44)), BorderThickness = new Thickness(1),
            RenderTransform = _translation, IsHitTestVisible = false
        };
        Children.Add(_selection);
        var buttons = new Grid();
        _buttons = new Button[titles.Length];
        for (int index = 0; index < titles.Length; index++)
        {
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int selected = index;
            var button = new Button
            {
                Content = titles[index], MinWidth = 0, MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, Background = null, BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(20), Padding = new Thickness(4, 0, 4, 0), FontSize = 13
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, titles[index]);
            button.Click += (_, _) => Select(selected);
            button.KeyDown += (_, args) =>
            {
                if (args.Key is not (Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)) return;
                int next = (SelectedIndex + (args.Key == Windows.System.VirtualKey.Left ? titles.Length - 1 : 1)) % titles.Length;
                Select(next); _buttons[next].Focus(FocusState.Keyboard); args.Handled = true;
            };
            Grid.SetColumn(button, index); buttons.Children.Add(button); _buttons[index] = button;
        }
        Children.Add(buttons);
        SizeChanged += (_, _) => MoveSelection(false);
        Loaded += (_, _) => MoveSelection(false);
        Unloaded += (_, _) => _motion?.Stop();
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _buttons.Length) return;
        bool changed = SelectedIndex != index; SelectedIndex = index;
        MoveSelection(changed);
        if (changed) SelectionChanged?.Invoke(index);
    }

    void MoveSelection(bool animate)
    {
        double width = Math.Max(0, ActualWidth / _buttons.Length);
        _selection.Width = Math.Max(0, width - 8);
        double target = width * SelectedIndex + 4;
        double start = _translation.X;
        _motion?.Stop(); _translation.X = target;
        for (int index = 0; index < _buttons.Length; index++)
        {
            _buttons[index].FontWeight = index == SelectedIndex ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(_buttons[index], index == SelectedIndex ? "当前页面" : "切换页面");
        }
        if (!animate || _reducedMotion() || width <= 0) return;
        var slide = new DoubleAnimation
        {
            From = start, To = target, Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, _translation); Storyboard.SetTargetProperty(slide, "X");
        _motion = new Storyboard(); _motion.Children.Add(slide); _motion.Begin();
    }
}
