using System.Numerics;
using GlassDesk.Core;
using GlassDesk.NativeBridge;
using Windows.UI;
using Windows.UI.Composition;

namespace GlassDesk.App;

/// <summary>One path-based character; expression and placement stay independent.</summary>
internal sealed class CompanionVisual : IDisposable
{
    const float Padding = 8;
    readonly NativeWidgetHost _host;
    readonly Compositor _compositor;
    readonly ContainerVisual _posture, _liquid;
    readonly ShapeVisual _trail;
    readonly CompositionPathGeometry _trailGeometry;
    readonly CompositionSpriteShape _trailShape;
    readonly ShapeVisual _face, _shadow;
    readonly CompositionPathGeometry _body;
    readonly Dictionary<(string, CompanionShapePose), CompositionPath> _paths = new();
    readonly CompositionEllipseGeometry[] _eyes = new CompositionEllipseGeometry[2];
    readonly CompositionSpriteShape[] _eyeShapes = new CompositionSpriteShape[2];
    readonly CompositionRoundedRectangleGeometry[] _lids = new CompositionRoundedRectangleGeometry[2];
    readonly CompositionSpriteShape[] _lidShapes = new CompositionSpriteShape[2];
    readonly CompositionColorBrush _bodyBrush, _eyeBrush, _shadowBrush;
    readonly CompositionSpriteShape _bodyShape, _shadowShape;
    readonly CompositionGeometricClip _faceClip;
    readonly InsetClip _coverClip;
    readonly CompositionPathGeometry _occlusionGeometry;
    readonly CompositionGeometricClip _occlusionClip;
    HideAnchor? _maskAnchor;
    CompanionCoverEdge? _maskEdge;
    CompanionVisualState? _state;
    CompanionShapePose _shapePose;
    string _appearance = CompanionAppearanceIds.Original;
    bool _reducedMotion, _pointerInside, _hasPath;
    Vector2 _pointerGaze, _edgeGaze;
    float _edgeTilt;
    public ContainerVisual Visual { get; }

    // An extended navigation body already owns the silhouette and its depth.
    public void UseIntegratedBody() => _shadow.Opacity = 0;

    public CompanionVisual(NativeWidgetHost host)
    {
        _host = host;
        _compositor = host.Compositor;
        foreach (string appearance in CompanionAppearanceIds.All)
            foreach (CompanionShapePose pose in Enum.GetValues<CompanionShapePose>())
                _paths[(appearance, pose)] = host.CreatePolygonPath(CompanionContour.GetPoints(appearance, pose)
                    .Select(point => point + new Vector2(Padding)).ToArray(), .78f);
        Visual = _compositor.CreateContainerVisual(); Visual.Size = new(24, 24);
        _liquid = _compositor.CreateContainerVisual(); _liquid.Size = new(24); _liquid.CenterPoint = new(12, 12, 0);
        _trail = _compositor.CreateShapeVisual(); _trail.Size = new(256); _trail.Offset = new(-128, -128, 0); _trail.Opacity = 0;
        _posture = _compositor.CreateContainerVisual(); _posture.Size = new(24, 24); _posture.CenterPoint = new(12, 12, 0);
        _face = _compositor.CreateShapeVisual(); _face.Size = new(40, 40); _face.Offset = new(-Padding, -Padding, 0);
        _shadow = _compositor.CreateShapeVisual(); _shadow.Size = new(40, 40); _shadow.Offset = new(-Padding, -Padding, 0);
        _body = _compositor.CreatePathGeometry();
        // Body, facial clip and shadow reference the same animated path geometry.
        _faceClip = _compositor.CreateGeometricClip(_body); _face.Clip = _faceClip;
        _coverClip = _compositor.CreateInsetClip(); _coverClip.LeftInset = _coverClip.TopInset = _coverClip.RightInset = -200;
        _occlusionGeometry = _compositor.CreatePathGeometry(); _occlusionClip = _compositor.CreateGeometricClip(_occlusionGeometry);
        _bodyBrush = _compositor.CreateColorBrush(Color.FromArgb(255, 32, 35, 40));
        _trailGeometry = _compositor.CreatePathGeometry();
        _trailShape = _compositor.CreateSpriteShape(_trailGeometry); _trailShape.FillBrush = _bodyBrush;
        _trail.Shapes.Add(_trailShape);
        _eyeBrush = _compositor.CreateColorBrush(Color.FromArgb(255, 250, 251, 252));
        _shadowBrush = _compositor.CreateColorBrush(Color.FromArgb(24, 0, 0, 0));
        _bodyShape = _compositor.CreateSpriteShape(_body); _bodyShape.FillBrush = _bodyBrush; _face.Shapes.Add(_bodyShape);
        _shadowShape = _compositor.CreateSpriteShape(_body); _shadowShape.FillBrush = _shadowBrush; _shadowShape.Offset = new(0, 1.3f); _shadow.Shapes.Add(_shadowShape);
        for (int index = 0; index < 2; index++)
        {
            var eye = _compositor.CreateEllipseGeometry(); eye.Radius = new(1.7f, 2.8f);
            var shape = _compositor.CreateSpriteShape(eye); shape.FillBrush = _eyeBrush;
            _eyes[index] = eye; _eyeShapes[index] = shape; _face.Shapes.Add(shape);
        }
        for (int index = 0; index < 2; index++)
        {
            var lid = _compositor.CreateRoundedRectangleGeometry(); lid.Size = new(6.4f, 4.8f); lid.CornerRadius = new(2.4f);
            var shape = _compositor.CreateSpriteShape(lid); shape.FillBrush = _bodyBrush;
            _lids[index] = lid; _lidShapes[index] = shape; _face.Shapes.Add(shape);
        }
        _posture.Children.InsertAtTop(_shadow); _posture.Children.InsertAtTop(_face);
        _liquid.Children.InsertAtTop(_posture); Visual.Children.InsertAtTop(_trail); Visual.Children.InsertAtTop(_liquid);
        ApplyPath(0); Visual.Opacity = 0;
    }

    public void SetState(CompanionVisualState? state, bool reducedMotion)
    {
        if (state == _state && reducedMotion == _reducedMotion) return;
        bool immediate = reducedMotion || _state is null;
        _state = state; _reducedMotion = reducedMotion;
        if (state is null) { Visual.Opacity = 0; StopAnimations(); return; }
        var color = Color.FromArgb(255, (byte)(state.Color >> 16), (byte)(state.Color >> 8), (byte)state.Color);
        _bodyBrush.Color = color;
        static double Linear(byte value) { double c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        double luminance = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        _eyeBrush.Color = luminance > .20 ? Color.FromArgb(255, 25, 28, 34) : Color.FromArgb(255, 250, 251, 252);
        int duration = immediate ? 0 : Math.Clamp(state.TransitionMs, 60, 500);
        string appearance = CompanionAppearanceIds.Normalize(state.AppearanceId);
        bool appearanceChanged = appearance != _appearance; _appearance = appearance;
        if (appearanceChanged || immediate) ApplyPath(duration);
        ApplyFacePose(duration);
        AnimateVector3(_posture, new(Math.Clamp(state.ScaleX, .92f, 1.06f), Math.Clamp(state.ScaleY, .92f, 1.06f), 1), duration);
        AnimateScalar(_posture, Math.Clamp(state.Tilt + _edgeTilt, -14, 14), duration);
    }

    /// <summary>Widget-local DIP anchor; size is the nominal body box, without ears.</summary>
    public void SetPose(CompanionPose pose, float size, HideAnchor? occluder = null, bool immediate = false, float opacity = 1)
    {
        CompanionShapePose shape = _reducedMotion ? CompanionShapePose.Rounded : pose.ShapePose;
        if (_shapePose != shape)
        {
            _shapePose = shape; int duration = immediate || _reducedMotion ? 0 : 180;
            ApplyPath(duration); ApplyFacePose(duration);
        }
        SetBounds(pose.BodyAnchor.X - size / 2, pose.BodyAnchor.Y - size / 2, size, opacity);
        CompanionCoverEdge? edge = occluder is null ? null : CompanionPlacementController.CoverEdge(occluder);
        bool navigationAnchor = occluder is not null && edge is null;
        if (occluder is not null && pose.OccluderId == occluder.StableId &&
            !(navigationAnchor && pose.PlacementMode == CompanionPlacementMode.Revealing))
        {
            if (edge is { } coverEdge)
            {
                if (_maskAnchor != occluder || _maskEdge != edge)
                {
                    _occlusionGeometry.Path = _host.CreatePolygonPath(CompanionContour.GetOcclusionClipPoints(occluder, coverEdge).ToArray(), 0);
                    _maskAnchor = occluder; _maskEdge = edge;
                }
                // Cached widget-space rounded-cover mask; only this affine
                // transform changes while the character enters or reveals.
                _occlusionClip.TransformMatrix = Matrix3x2.CreateTranslation(-pose.BodyAnchor + new Vector2(size / 2)) * Matrix3x2.CreateScale(24 / size);
                Visual.Clip = _occlusionClip;
            }
            else
            {
                float cut = (occluder.VisibleEyeRegion.Bottom - pose.BodyAnchor.Y) * 24 / size + 12;
                _coverClip.BottomInset = 24 - cut; Visual.Clip = _coverClip;
            }
        }
        else Visual.Clip = null;
    }

    public void SetLiquid(CompanionLiquidFrame frame, Vector2 anchor, float size)
    {
        bool active = frame.Active && !_reducedMotion && _state is not null;
        _liquid.Scale = active ? new(frame.Scale, 1) : Vector3.One;
        _liquid.Offset = active ? new(frame.HeadOffset * (24 / size), 0) : Vector3.Zero;
        _trail.Opacity = active ? frame.Opacity : 0;
        float unit = 24 / size;
        if (active && frame.Trail.Count > 0)
        {
            Vector2[] contour = CompanionLiquidMotion.TrailContour(frame, anchor, size);
            for (int i = 0; i < contour.Length; i++) contour[i] = (contour[i] - anchor) * unit + new Vector2(140);
            _trailGeometry.Path = _host.CreatePolygonPath(contour, .9f);
        }
    }

    void ApplyPath(int duration)
    {
        CompositionPath target = _paths[(_appearance, _shapePose)];
        if (!_hasPath || _reducedMotion || duration == 0)
        {
            _body.StopAnimation("Path"); _body.Path = target; _hasPath = true; return;
        }
        using var easing = _compositor.CreateCubicBezierEasingFunction(new(.22f, 1), new(.36f, 1));
        using var animation = _compositor.CreatePathKeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue"); animation.InsertKeyFrame(1, target, easing);
        animation.Duration = TimeSpan.FromMilliseconds(duration); _body.StartAnimation("Path", animation);
    }

    void ApplyFacePose(int duration)
    {
        if (_state is not { } state) return;
        for (int index = 0; index < 2; index++)
        {
            Vector2 center = Landmark(new(index == 0 ? 7.9f : 16.1f, 11.7f));
            AnimateVector2(_eyes[index], "Center", center, duration); _eyeShapes[index].CenterPoint = center;
            Vector2 radius = state.Expression switch
            {
                CompanionExpression.Curious => index == 0 ? new(1.55f, 2.25f) : new(2.1f, 3.45f),
                CompanionExpression.Happy => new(2.55f, 2.3f), CompanionExpression.Sleepy => new(2.05f, 1.65f),
                CompanionExpression.Surprised => new(2.75f, 3.4f), CompanionExpression.Blink => new(2.2f, .25f), _ => new(1.7f, 2.8f)
            };
            Vector2 basis = CompanionContour.TransformPoint(new(12 + radius.X, 12 + radius.Y), _shapePose) - CompanionContour.TransformPoint(new(12, 12), _shapePose);
            AnimateVector2(_eyes[index], "Radius", new(Math.Abs(basis.X), Math.Abs(basis.Y)), duration);
            float angle = state.Expression == CompanionExpression.Curious ? (index == 0 ? -8 : 7) : state.Expression == CompanionExpression.Happy ? (index == 0 ? -12 : 12) : 0;
            AnimateScalar(_eyeShapes[index], angle, duration);
        }
        ApplyGaze(duration);
    }
    Vector2 Landmark(Vector2 point) => CompanionContour.TransformPoint(point, _shapePose) + new Vector2(Padding);
    public void SetGaze(Vector2 direction, bool inside)
    {
        Vector2 gaze = inside ? Vector2.Clamp(direction, new(-1.8f, -1.25f), new(1.8f, 1.25f)) : Vector2.Zero;
        if (_pointerInside == inside && Vector2.DistanceSquared(gaze, _pointerGaze) < .025f) return;
        _pointerInside = inside; _pointerGaze = gaze; ApplyGaze(160);
    }
    public void SetEdgeGaze(Vector2 direction) { if (direction == _edgeGaze) return; _edgeGaze = direction; ApplyGaze(240); }
    public void SetEdgeTilt(float degrees)
    {
        if (_edgeTilt == degrees) return;
        _edgeTilt = degrees; AnimateScalar(_posture, Math.Clamp((_state?.Tilt ?? 0) + degrees, -14, 14), 240);
    }
    public void ClearPointerGaze()
    {
        if (!_pointerInside) return;
        _pointerInside = false; _pointerGaze = Vector2.Zero; ApplyGaze(0);
    }
    void ApplyGaze(int duration)
    {
        if (_state is not { } state) return;
        Vector2 direction = _pointerInside ? _pointerGaze : new(state.EyeOffsetX + _edgeGaze.X, state.EyeOffsetY + _edgeGaze.Y);
        direction = Vector2.Clamp(direction, new(-1.9f, -1.6f), new(1.9f, 1.6f));
        for (int index = 0; index < 2; index++)
        {
            AnimateVector2(_eyeShapes[index], "Offset", direction, duration);
            float lidY = state.Expression switch { CompanionExpression.Happy => 11.5f, CompanionExpression.Sleepy => 7.2f, _ => 18 };
            AnimateVector2(_lidShapes[index], "Offset", Landmark(new(index == 0 ? 4.7f : 12.9f, lidY)) + direction, duration);
        }
    }
    void AnimateVector2(CompositionObject target, string property, Vector2 value, int duration)
    {
        if (_reducedMotion || duration == 0)
        {
            target.StopAnimation(property);
            if (target is CompositionEllipseGeometry geometry) { if (property == "Center") geometry.Center = value; else geometry.Radius = value; }
            else if (target is CompositionSpriteShape shape) shape.Offset = value;
            return;
        }
        using var easing = _compositor.CreateCubicBezierEasingFunction(new(.22f, 1), new(.36f, 1));
        using var animation = _compositor.CreateVector2KeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue"); animation.InsertKeyFrame(1, value, easing);
        animation.Duration = TimeSpan.FromMilliseconds(duration); target.StartAnimation(property, animation);
    }
    void AnimateVector3(Visual target, Vector3 value, int duration)
    {
        if (_reducedMotion || duration == 0) { target.StopAnimation("Scale"); target.Scale = value; return; }
        using var animation = _compositor.CreateVector3KeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue"); animation.InsertKeyFrame(1, value);
        animation.Duration = TimeSpan.FromMilliseconds(duration); target.StartAnimation("Scale", animation);
    }
    void AnimateScalar(CompositionObject target, float value, int duration)
    {
        if (_reducedMotion || duration == 0)
        {
            target.StopAnimation("RotationAngleInDegrees");
            if (target is Visual visual) visual.RotationAngleInDegrees = value;
            else if (target is CompositionSpriteShape shape) shape.RotationAngleInDegrees = value;
            return;
        }
        using var animation = _compositor.CreateScalarKeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue"); animation.InsertKeyFrame(1, value);
        animation.Duration = TimeSpan.FromMilliseconds(duration); target.StartAnimation("RotationAngleInDegrees", animation);
    }
    public void SetBounds(float x, float y, float size, float opacity)
    {
        Visual.Offset = new(x, y, 0); Visual.Scale = new(size / 24, size / 24, 1); Visual.Opacity = _state is null ? 0 : opacity;
    }
    void StopAnimations()
    {
        _liquid.Scale = Vector3.One; _liquid.Offset = Vector3.Zero; _trail.Opacity = 0;
        _body.StopAnimation("Path"); _posture.StopAnimation("Scale"); _posture.StopAnimation("RotationAngleInDegrees");
        foreach (var eye in _eyes) { eye.StopAnimation("Center"); eye.StopAnimation("Radius"); }
        foreach (var shape in _eyeShapes) { shape.StopAnimation("Offset"); shape.StopAnimation("RotationAngleInDegrees"); }
        foreach (var shape in _lidShapes) shape.StopAnimation("Offset");
    }
    public void Dispose()
    {
        StopAnimations(); Visual.Children.RemoveAll(); _liquid.Children.RemoveAll(); _posture.Children.RemoveAll(); _face.Shapes.Clear(); _shadow.Shapes.Clear();
        _trail.Shapes.Clear();
        _trailShape.Dispose(); _trailGeometry.Dispose();
        foreach (var shape in _eyeShapes) shape.Dispose(); foreach (var geometry in _eyes) geometry.Dispose();
        foreach (var shape in _lidShapes) shape.Dispose(); foreach (var geometry in _lids) geometry.Dispose();
        _bodyShape.Dispose(); _shadowShape.Dispose(); _faceClip.Dispose(); _coverClip.Dispose(); _occlusionClip.Dispose(); _occlusionGeometry.Dispose(); _body.Dispose();
        _paths.Clear(); _bodyBrush.Dispose(); _eyeBrush.Dispose(); _shadowBrush.Dispose(); _face.Dispose(); _shadow.Dispose(); _trail.Dispose(); _posture.Dispose(); _liquid.Dispose(); Visual.Dispose();
    }
}
