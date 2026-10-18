using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace GlassDesk.App;

/// <summary>Settings controls keep the reference's two columns and stack on a narrow pane.</summary>
internal sealed class SettingsColumns : Panel
{
    int _columns;
    const double Gap = 12;
    public SettingsColumns(IEnumerable<FrameworkElement> controls)
    { foreach (var control in controls) Children.Add(control); }
    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsFinite(available.Width) ? available.Width : 760;
        _columns = width >= 300 * 2 + Gap ? 2 : 1;
        double itemWidth = Math.Max(0, (width - Gap * (_columns - 1)) / _columns), height = 0;
        for (int row = 0; row < Children.Count; row += _columns)
        {
            double rowHeight = 0;
            for (int column = 0; column < _columns && row + column < Children.Count; column++)
            { var child = Children[row + column]; child.Measure(new(itemWidth, double.PositiveInfinity)); rowHeight = Math.Max(rowHeight, child.DesiredSize.Height); }
            height += rowHeight + (row == 0 ? 0 : Gap);
        }
        return new(width, height);
    }
    protected override Size ArrangeOverride(Size final)
    {
        double itemWidth = Math.Max(0, (final.Width - Gap * (_columns - 1)) / _columns), top = 0;
        for (int row = 0; row < Children.Count; row += _columns)
        {
            double height = 0;
            for (int column = 0; column < _columns && row + column < Children.Count; column++) height = Math.Max(height, Children[row + column].DesiredSize.Height);
            for (int column = 0; column < _columns && row + column < Children.Count; column++) Children[row + column].Arrange(new(column * (itemWidth + Gap), top, itemWidth, height));
            top += height + Gap;
        }
        return final;
    }
}
