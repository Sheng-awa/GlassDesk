using System.Numerics;
using Windows.UI;
using Windows.UI.Composition;

namespace GlassDesk.App;

/// <summary>Peek's fixed top face. Pointer motion only animates the eyes on the compositor.</summary>
internal sealed class PeekCompanionVisual : IDisposable
{
    readonly Compositor _compositor;
    readonly ShapeVisual _body;
    readonly ContainerVisual _gaze;
    readonly ShapeVisual[] _eyes = new ShapeVisual[2];
    readonly CompositionRoundedRectangleGeometry _bodyGeometry;
    readonly CompositionRoundedRectangleGeometry[] _eyeGeometry = new CompositionRoundedRectangleGeometry[2];
    readonly CompositionColorBrush _bodyInk, _eyeInk;
    Vector3 _target;
    bool _reduced;
    public ContainerVisual Visual { get; }

    public PeekCompanionVisual(Compositor compositor)
    {
        _compositor = compositor;
        Visual = compositor.CreateContainerVisual(); Visual.Size = new(58, 28); Visual.Opacity = 0;
        _body = compositor.CreateShapeVisual(); _body.Size = Visual.Size;
        _bodyGeometry = compositor.CreateRoundedRectangleGeometry();
        _bodyGeometry.Size = Visual.Size; _bodyGeometry.CornerRadius = new(14);
        _bodyInk = compositor.CreateColorBrush(); _eyeInk = compositor.CreateColorBrush();
        var body = compositor.CreateSpriteShape(_bodyGeometry); body.FillBrush = _bodyInk; _body.Shapes.Add(body);
        Visual.Children.InsertAtTop(_body);
        _gaze = compositor.CreateContainerVisual(); _gaze.Size = Visual.Size; Visual.Children.InsertAtTop(_gaze);
        for (int i = 0; i < 2; i++)
        {
            var eye = compositor.CreateShapeVisual(); eye.Size = new(5, 9); eye.Offset = new(22 + i * 9, 9.5f, 0);
            var geometry = compositor.CreateRoundedRectangleGeometry(); geometry.Size = eye.Size; geometry.CornerRadius = new(2.5f);
            var shape = compositor.CreateSpriteShape(geometry); shape.FillBrush = _eyeInk; eye.Shapes.Add(shape);
            _eyes[i] = eye; _eyeGeometry[i] = geometry; _gaze.Children.InsertAtTop(eye);
        }
    }

    public void Present(float x, float y, float opacity, uint body, uint ink, bool reduced)
    {
        Visual.Offset = new(x, y, 0); Visual.Opacity = opacity;
        _bodyInk.Color = ColorOf(body); _eyeInk.Color = ColorOf(ink); _reduced = reduced;
        if (reduced || opacity <= .001f) Gaze(0, 0, false);
    }

    public void Gaze(float x, float y, bool enabled)
    {
        var target = enabled && !_reduced
            ? new Vector3(Math.Clamp((x - Visual.Offset.X - 29) / 65, -2.5f, 2.5f), Math.Clamp((y - Visual.Offset.Y - 14) / 80, -2, 2), 0)
            : Vector3.Zero;
        if (_reduced) { _gaze.StopAnimation("Offset"); _gaze.Offset = target; _target = target; return; }
        if (Vector3.DistanceSquared(target, _target) < .0025f) return;
        _target = target;
        using var easing = _compositor.CreateCubicBezierEasingFunction(new(.22f, 1), new(.36f, 1));
        using var animation = _compositor.CreateVector3KeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue"); animation.InsertKeyFrame(1, target, easing);
        animation.Duration = TimeSpan.FromMilliseconds(200); _gaze.StartAnimation("Offset", animation);
    }

    static Color ColorOf(uint ink) => Color.FromArgb((byte)(ink >> 24), (byte)(ink >> 16), (byte)(ink >> 8), (byte)ink);

    public void Dispose()
    {
        _gaze.StopAnimation("Offset"); _gaze.Children.RemoveAll(); Visual.Children.RemoveAll();
        foreach (var eye in _eyes) { foreach (var shape in eye.Shapes) shape.Dispose(); eye.Shapes.Clear(); eye.Dispose(); }
        foreach (var geometry in _eyeGeometry) geometry.Dispose();
        foreach (var shape in _body.Shapes) shape.Dispose(); _body.Shapes.Clear();
        _body.Dispose(); _bodyGeometry.Dispose(); _bodyInk.Dispose(); _eyeInk.Dispose(); _gaze.Dispose(); Visual.Dispose();
    }
}
