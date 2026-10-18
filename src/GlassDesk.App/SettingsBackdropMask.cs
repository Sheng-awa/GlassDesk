using System.Numerics;
using Windows.UI.Composition;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.UI;

namespace GlassDesk.App;

/// <summary>GPU-only opacity mask for the two separate desktop acrylic surfaces.</summary>
internal sealed class SettingsBackdropMask : IDisposable
{
    readonly ContainerVisual _root;
    readonly ShapeVisual _pane, _navigation;
    readonly CompositionRoundedRectangleGeometry _paneGeometry, _navigationGeometry;
    readonly CompositionSpriteShape _paneShape, _navigationShape;
    readonly CompositionColorBrush _white;
    readonly CompositionVisualSurface _surface;
    readonly CompositionSurfaceBrush _surfaceBrush;
    BoundsKey? _bounds;
    bool _disposed;
    public Compositor Compositor { get; }
    public CompositionMaskBrush Brush { get; }

    public SettingsBackdropMask(Compositor compositor)
    {
        Compositor = compositor;
        _root = compositor.CreateContainerVisual();
        _white = compositor.CreateColorBrush(Color.FromArgb(255, 255, 255, 255));
        _pane = compositor.CreateShapeVisual(); _navigation = compositor.CreateShapeVisual();
        _paneGeometry = compositor.CreateRoundedRectangleGeometry();
        _navigationGeometry = compositor.CreateRoundedRectangleGeometry();
        _paneShape = compositor.CreateSpriteShape(_paneGeometry); _paneShape.FillBrush = _white;
        _navigationShape = compositor.CreateSpriteShape(_navigationGeometry); _navigationShape.FillBrush = _white;
        _pane.Shapes.Add(_paneShape); _navigation.Shapes.Add(_navigationShape);
        _root.Children.InsertAtTop(_pane); _root.Children.InsertAtTop(_navigation);
        _surface = compositor.CreateVisualSurface(); _surface.SourceVisual = _root; _surface.SourceSize = new(1, 1);
        _surfaceBrush = compositor.CreateSurfaceBrush(_surface); _surfaceBrush.Stretch = CompositionStretch.Fill;
        Brush = compositor.CreateMaskBrush(); Brush.Mask = _surfaceBrush;
    }

    public void SetSource(CompositionBrush source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Brush.Source = source;
    }

    public void Update(FrameworkElement root, FrameworkElement pane, FrameworkElement navigation)
    {
        if (_disposed || root.ActualWidth <= 0 || root.ActualHeight <= 0) return;
        float scale = (float)(root.XamlRoot?.RasterizationScale ?? 1);
        var panePosition = pane.TransformToVisual(root).TransformPoint(new Point());
        var navPosition = navigation.TransformToVisual(root).TransformPoint(new Point());
        // The mask texture is realized at physical-pixel size, then stretched over
        // the complete backdrop target. Ratios preserve exact XAML DIP placement.
        var key = new BoundsKey(new((float)root.ActualWidth * scale, (float)root.ActualHeight * scale),
            new((float)panePosition.X * scale, (float)panePosition.Y * scale),
            new((float)pane.ActualWidth * scale, (float)pane.ActualHeight * scale),
            new((float)navPosition.X * scale, (float)navPosition.Y * scale),
            new((float)navigation.ActualWidth * scale, (float)navigation.ActualHeight * scale), scale);
        if (_bounds == key) return;
        _bounds = key; _root.Size = key.RootSize; _surface.SourceSize = key.RootSize;
        Place(_pane, _paneGeometry, key.PanePosition, key.PaneSize, 24 * scale);
        Place(_navigation, _navigationGeometry, key.NavPosition, key.NavSize, 28 * scale);
    }

    static void Place(ShapeVisual visual, CompositionRoundedRectangleGeometry geometry, Vector2 position, Vector2 size, float radius)
    {
        size = Vector2.Max(size, Vector2.Zero);
        visual.Offset = new(position, 0); visual.Size = geometry.Size = size;
        geometry.CornerRadius = new(Math.Max(0, Math.Min(radius, Math.Min(size.X, size.Y) / 2)));
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        Brush.Source = null; Brush.Mask = null;
        _surfaceBrush.Surface = null; _surface.SourceVisual = null;
        _root.Children.RemoveAll(); _pane.Shapes.Clear(); _navigation.Shapes.Clear();
        Brush.Dispose(); _surfaceBrush.Dispose(); _surface.Dispose();
        _paneShape.Dispose(); _navigationShape.Dispose(); _paneGeometry.Dispose(); _navigationGeometry.Dispose();
        _white.Dispose(); _pane.Dispose(); _navigation.Dispose(); _root.Dispose();
    }

    readonly record struct BoundsKey(Vector2 RootSize, Vector2 PanePosition, Vector2 PaneSize,
        Vector2 NavPosition, Vector2 NavSize, float Scale);
}
