using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace GlassDesk.App;

/// <summary>One finite page viewport; horizontal continuity follows the navigation's live position.</summary>
internal sealed class SettingsPageDeck : Panel
{
    readonly RectangleGeometry _clip = new();
    readonly List<ScrollViewer> _pages = new();
    double _progress;
    int _selected;

    public SettingsPageDeck(IEnumerable<FrameworkElement> pages)
    {
        Clip = _clip;
        foreach (var page in pages)
        {
            var container = new ScrollViewer
            {
                Content = page, IsTabStop = false, RenderTransform = new TranslateTransform(),
                HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(0, 0, 4, 8)
            };
            _pages.Add(container); Children.Add(container);
        }
        SetSelectedIndex(0);
    }

    public void SetProgress(double progress)
    {
        progress = Math.Clamp(progress, 0, Math.Max(0, _pages.Count - 1));
        if (Math.Abs(progress - _progress) < .0001) return;
        _progress = progress;
        InvalidateArrange();
    }

    public void SetSelectedIndex(int selected)
    {
        _selected = Math.Clamp(selected, 0, Math.Max(0, _pages.Count - 1));
        for (int index = 0; index < _pages.Count; index++) _pages[index].IsEnabled = index == _selected;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : 640;
        double height = double.IsFinite(availableSize.Height) ? Math.Max(0, availableSize.Height) : 420;
        foreach (var page in _pages) page.Measure(new Size(width, height));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _clip.Rect = new Rect(0, 0, finalSize.Width, finalSize.Height);
        for (int index = 0; index < _pages.Count; index++)
        {
            var page = _pages[index];
            page.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            ((TranslateTransform)page.RenderTransform).X = (index - _progress) * (finalSize.Width + 24);
        }
        return finalSize;
    }
}
