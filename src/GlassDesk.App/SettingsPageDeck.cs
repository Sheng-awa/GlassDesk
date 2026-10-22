using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace GlassDesk.App;

/// <summary>Retains page controls and scroll; only the selected page participates in layout and input.</summary>
internal sealed class SettingsPageDeck : Grid
{
    readonly List<ScrollViewer> _pages = [];
    readonly Func<bool> _reduced;
    Storyboard? _transition;
    int _selected = -1;

    public SettingsPageDeck(IEnumerable<FrameworkElement> pages, Func<bool> reduced)
    {
        _reduced = reduced;
        foreach (var page in pages)
        {
            var container = new ScrollViewer
            {
                Content = page, IsTabStop = false, Visibility = Visibility.Collapsed,
                HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(4, 0, 12, 24)
            };
            _pages.Add(container); Children.Add(container);
        }
        SetSelectedIndex(0);
    }

    public void SetSelectedIndex(int selected)
    {
        if (_pages.Count == 0) return;
        selected = Math.Clamp(selected, 0, _pages.Count - 1);
        if (selected == _selected) return;
        bool animate = _selected >= 0 && !_reduced() && IsLoaded;
        StopTransition();
        _selected = selected;
        for (int index = 0; index < _pages.Count; index++)
            _pages[index].Visibility = index == selected ? Visibility.Visible : Visibility.Collapsed;
        if (!animate) return;
        var fade = new DoubleAnimation { From = .45, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(160)) };
        Storyboard.SetTarget(fade, _pages[selected]); Storyboard.SetTargetProperty(fade, "Opacity");
        _transition = new Storyboard(); _transition.Children.Add(fade); _transition.Begin();
    }

    public void StopTransition()
    {
        _transition?.Stop(); _transition = null;
        foreach (var page in _pages) page.Opacity = 1;
    }
}
