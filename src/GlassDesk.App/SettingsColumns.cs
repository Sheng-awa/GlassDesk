using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace GlassDesk.App;

/// <summary>Only related choices share columns; ordinary settings stay in one reading column.</summary>
internal sealed class SettingsColumns : Panel
{
    int _columns;
    const double Gap = 12;
    readonly int _maximumColumns;
    readonly double _minimumColumnWidth;
    public SettingsColumns(IEnumerable<FrameworkElement> controls, int maximumColumns = 2, double minimumColumnWidth = 300)
    {
        _maximumColumns = Math.Max(1, maximumColumns); _minimumColumnWidth = Math.Max(44, minimumColumnWidth);
        foreach (var control in controls) Children.Add(control);
    }
    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsFinite(available.Width) ? available.Width : 760;
        _columns = Math.Clamp((int)Math.Floor((width + Gap) / (_minimumColumnWidth + Gap)), 1, _maximumColumns);
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
