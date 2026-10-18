using System.Numerics;
using GlassDesk.Core;
using GlassDesk.NativeBridge;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.ViewManagement;

namespace GlassDesk.App;

/// <summary>The navigation shell is the character's extensible body, with one face and no visible labels.</summary>
internal sealed class DetailsNavigationVisual : IDisposable
{
    readonly ShapeVisual _body;
    readonly CompositionRoundedRectangleGeometry _bodyGeometry;
    readonly CompositionColorBrush _bodyInk;
    readonly ShapeVisual[] _bars = new ShapeVisual[5];
    readonly CompositionRoundedRectangleGeometry[] _geometry = new CompositionRoundedRectangleGeometry[5];
    readonly CompositionColorBrush[] _ink = new CompositionColorBrush[5];
    readonly CompanionVisual _face;
    readonly AccessibilitySettings _accessibility = new();
    readonly UISettings _uiSettings = new();
    readonly NativeWidgetHost _host;
    readonly SpriteVisual _satellite, _semantic;
    readonly CompositionSurfaceBrush _satelliteBrush, _semanticBrush;
    CompositionDrawingSurface? _satelliteSurface, _semanticSurface;
    string _pageId = DetailsPageController.Music;
    (string Text, bool Selected, bool HighContrast, uint Dpi, bool Light)? _semanticKey;
    bool _semanticLight;
    public const int WidgetsAction = 111;
    public event Action<string>? RealPageRequested;
    CompanionVisualState? _state;
    float _width = 360, _top = 16;
    Vector2 _pointer;
    bool _pointerPresent, _reducedMotion, _disposed, _followMouse = true;
    public ContainerVisual Visual { get; }
    public NavigationInteractionModel Model { get; } = new();
    public bool NeedsTick => Model.NeedsTick;
    float ShellWidth => NavigationRibbonLayout.Width(Model.Expansion);
    public FloatRect Bounds => new((_width - ShellWidth) / 2, _top, ShellWidth, 48);
    public FloatRect ExpandedBounds => new((_width - 264) / 2, _top, 264, 48);

    public DetailsNavigationVisual(NativeWidgetHost host)
    {
        _host = host;
        var c = host.Compositor;
        Visual = c.CreateContainerVisual();
        _body = c.CreateShapeVisual(); _bodyGeometry = c.CreateRoundedRectangleGeometry(); _bodyGeometry.CornerRadius = new(24);
        _bodyInk = c.CreateColorBrush();
        var body = c.CreateSpriteShape(_bodyGeometry); body.FillBrush = _bodyInk; _body.Shapes.Add(body);
        Visual.Children.InsertAtTop(_body);
        for (int i = 0; i < 5; i++)
        {
            _geometry[i] = c.CreateRoundedRectangleGeometry(); _geometry[i].CornerRadius = new(4);
            _ink[i] = c.CreateColorBrush(Color.FromArgb(255, 250, 251, 252));
            var bar = c.CreateSpriteShape(_geometry[i]); bar.FillBrush = _ink[i];
            _bars[i] = c.CreateShapeVisual(); _bars[i].Shapes.Add(bar); Visual.Children.InsertAtTop(_bars[i]);
        }
        _face = new CompanionVisual(host); _face.UseIntegratedBody(); Visual.Children.InsertAtTop(_face.Visual);
        _satellite = c.CreateSpriteVisual(); _satelliteBrush = c.CreateSurfaceBrush(); _satellite.Brush = _satelliteBrush;
        _satellite.Size = new(28,28); Visual.Children.InsertAtTop(_satellite);
        _semantic = c.CreateSpriteVisual(); _semanticBrush = c.CreateSurfaceBrush(); _semantic.Brush = _semanticBrush;
        _semantic.Size = new(264,20); Visual.Children.InsertAtTop(_semantic);
        Visual.Opacity = 0; Present();
    }
    public void SetRealPage(string id) { _pageId = DetailsPageController.Normalize(id); Present(); }
    public void SetSemanticInk(bool light) { if (_semanticLight == light) return; _semanticLight = light; Present(); }
    FloatRect SatelliteHit => new(Bounds.Right, _top+2, 44,44);
    public void SetCharacter(CompanionVisualState? state, bool reducedMotion, bool followMouse)
    {
        _state = state; _reducedMotion = reducedMotion || !_uiSettings.AnimationsEnabled; _followMouse = followMouse;
        if (!_followMouse || _reducedMotion) _face.ClearPointerGaze();
        _face.SetState(state, _reducedMotion); Present();
    }
    public void SetLayout(float width, float top = 16)
    { _width = Math.Max(288, width); _top = top; Visual.Size = new(_width, 80); Present(); }
    public void BeginOpen(long sequence, double now, bool reducedMotion, bool companionEnabled)
    {
        _pointerPresent = false;
        Model.BeginOpen(sequence, now, reducedMotion || !_uiSettings.AnimationsEnabled, companionEnabled, keepExpanded: true);
        Visual.Opacity = 1; Present();
    }
    public void Close() { _pointerPresent = false; Model.Close(); Visual.Opacity = 0; }
    public void SetReducedMotion(bool reducedMotion, double now)
    { _reducedMotion = reducedMotion || !_uiSettings.AnimationsEnabled; Model.SetReducedMotion(_reducedMotion, now); _face.SetState(_state, _reducedMotion); Present(); }
    public void PointerMoved(float x, float y, double now)
    { _pointer = new(x, y); _pointerPresent = true; UpdatePointer(now); Present(); }
    public void PointerExited(double now) { _pointerPresent = false; UpdatePointer(now); Present(); }
    public int HitTest(float x, float y)
    {
        if (Model.IsActive && Model.Expansion >= .92 && SatelliteHit.Contains(x,y)) return WidgetsAction;
        if (!Model.IsActive || !InsideRounded(Bounds, x, y)) return -1;
        if (Model.Expansion < .92) return WidgetActions.NavigationShell;
        return WidgetActions.NavigationFirst + NavigationRibbonLayout.Hit(x - _width / 2, Model.CenterIndex);
    }
    public bool Activate(int actionId, double now)
    {
        if (!Model.IsActive) return false;
        if (actionId == WidgetsAction && Model.Expansion >= .92)
        { RealPageRequested?.Invoke(DetailsPageController.Widgets); Present(); return true; }
        if (actionId == WidgetActions.NavigationShell) { Model.OpenImmediately(now); Present(); return true; }
        int i = actionId - WidgetActions.NavigationFirst;
        if (i is < 0 or > 4) return false;
        Model.SelectPreview(PreviewNavigationItems.All[i].Id, now);
        if (i == 0) RealPageRequested?.Invoke(DetailsPageController.Music);
        Present(); return true;
    }
    public void Tick(double now)
    {
        if (_disposed || !Model.IsActive) return;
        Model.Tick(now); UpdatePointer(now); Present();
    }
    public IReadOnlyList<AccessibleControl> GetAccessibleControls()
    {
        if (!Model.IsActive) return Array.Empty<AccessibleControl>();
        var b = Bounds;
        if (Model.Expansion < .92)
            return new[] { new AccessibleControl(WidgetActions.NavigationShell, "展开导航", b.X, b.Y, b.Width, b.Height) };
        string page = _pageId == DetailsPageController.Widgets ? "小组件" : "音乐";
        var entries = PreviewNavigationItems.All.Select((entry, i) => new AccessibleControl(entry.ActionId,
            $"{entry.Label}{(i==0 ? "" : "，预览")}{(entry.Id == Model.State.SelectedPreviewItemId ? "，视觉已选中" : "")}，当前页面：{page}",
            _width / 2 + NavigationRibbonLayout.ItemOffset(i, Model.CenterIndex) - 19, _top, 38, 48)).ToList();
        var satellite = SatelliteHit;
        entries.Add(new(WidgetsAction,$"小组件{(_pageId==DetailsPageController.Widgets ? "，当前页面" : "，打开页面")}",
            satellite.X,satellite.Y,satellite.Width,satellite.Height));
        return entries;
    }
    void UpdatePointer(double now)
    {
        bool inside = _pointerPresent && InsideRounded(Bounds, _pointer.X, _pointer.Y);
        Model.SetPointerInside(inside, now);
        int hit = inside ? HitTest(_pointer.X, _pointer.Y) - WidgetActions.NavigationFirst : -1;
        Model.SetHoveredItem(hit is >= 0 and < 5 ? PreviewNavigationItems.All[hit].Id : null, now);
        float gaze = hit is >=0 and <5 ? Math.Clamp(NavigationRibbonLayout.ItemOffset(hit,Model.CenterIndex)/120,-.7f,.7f)
            : _pointerPresent && SatelliteHit.Contains(_pointer.X,_pointer.Y) ? .7f
            : _pageId == DetailsPageController.Widgets ? .35f : 0;
        _face.SetGaze(new(gaze,0), _followMouse && !_reducedMotion);
    }
    void Present()
    {
        float expansion = Math.Clamp((float)Model.Expansion, 0, 1.08f), width = Math.Max(48, ShellWidth);
        _body.Offset = new((_width - width) / 2, _top, 0); _body.Size = _bodyGeometry.Size = new(width, 48);
        bool hc = _accessibility.HighContrast;
        Color body = hc ? _uiSettings.UIElementColor(UIElementType.Window) : ToColor(_state?.Color ?? 0xFF202328);
        Color eye = hc ? _uiSettings.UIElementColor(UIElementType.WindowText) : Contrast(body);
        _bodyInk.Color = body;
        for (int i = 0; i < 5; i++)
        {
            float offset = NavigationRibbonLayout.ItemOffset(i, Model.CenterIndex);
            float distance = Math.Abs(offset) / NavigationRibbonLayout.Spacing;
            float emphasis = (float)Model.ItemEmphasis[i];
            float opacity = Math.Clamp(distance / .65f, 0, 1) * Math.Clamp(1 - distance * .29f, .15f, 1)
                * Math.Clamp((2.5f - distance) / .5f, 0, 1) * expansion;
            float height = 14 + 9 * emphasis + 6 * Math.Max(0, 1 - distance / 3);
            float w = 7 + 4 * emphasis;
            _bars[i].Offset = new(_width / 2 + offset * Math.Min(1, expansion) - w / 2, _top + (48 - height) / 2, 0);
            _bars[i].Size = _geometry[i].Size = new(w, height); _bars[i].Opacity = opacity; _ink[i].Color = eye;
        }
        _face.SetBounds(_width / 2 - 24, _top, 48, _state is null ? 0 : 1);
        _face.SetState(_state is null ? null : _state with { Color = (uint)body.A << 24 | (uint)body.R << 16 | (uint)body.G << 8 | body.B }, _reducedMotion);
        PresentSemantics(hc);
    }
    void PresentSemantics(bool highContrast)
    {
        bool selected = _pageId == DetailsPageController.Widgets;
        var hovered = PreviewNavigationItems.All.FirstOrDefault(e => e.Id == Model.State.HoveredItemId);
        string title = selected ? "小组件" : "音乐";
        if (_pointerPresent && SatelliteHit.Contains(_pointer.X,_pointer.Y)) title += selected ? "" : " · 小组件";
        else if (hovered is not null && hovered.Id != _pageId) title += " · " + hovered.Label + (hovered.Id=="music"?"":"（预览）");
        var key = (title,selected,highContrast,_host.Context.Dpi,_semanticLight);
        if (_semanticKey != key)
        {
            uint ink = highContrast ? Argb(_uiSettings.UIElementColor(UIElementType.WindowText)) : 0xff20252c;
            uint fill = highContrast ? Argb(_uiSettings.UIElementColor(UIElementType.Window)) : 0xe8f8fafc;
            uint ring = highContrast ? ink : 0xff1689f9;
            var ops = new List<SurfaceOp> { new() {Kind=SurfaceOpKind.Ellipse,X=0,Y=0,Width=28,Height=28,Color=selected?ring:fill} };
            if (selected) ops.Add(new() {Kind=SurfaceOpKind.Ellipse,X=2,Y=2,Width=24,Height=24,Color=fill});
            for(int i=0;i<4;i++) ops.Add(new() {Kind=SurfaceOpKind.RoundedRectangle,X=8+(i%2)*7,Y=8+(i/2)*7,Width=5,Height=5,Radius=1,Color=ink});
            Replace(ref _satelliteSurface,_satelliteBrush,_host.RenderSurface(28,28,ops));
            var caption = new List<SurfaceOp> {
                SurfaceOp.Typography(title,TextRole.NavigationSemantic,0,2,264,16,
                    highContrast ? ink : TypographyCatalog.Ink(InkRole.Primary,_semanticLight),center:true)
            };
            Replace(ref _semanticSurface,_semanticBrush,_host.RenderSurface(264,20,caption));
            _semanticKey=key;
        }
        _satellite.Offset=new(Bounds.Right+8,_top+10,0);
        _satellite.Opacity=Model.IsActive && Model.Expansion>=.92 ? 1 : 0;
        _semantic.Offset=new((_width-264)/2,76,0);
        _semantic.Opacity=Model.IsActive && Model.Expansion>=.92 ? 1 : 0;
    }
    static uint Argb(Color c)=>(uint)c.A<<24|(uint)c.R<<16|(uint)c.G<<8|c.B;
    static void Replace(ref CompositionDrawingSurface? owned,CompositionSurfaceBrush brush,CompositionDrawingSurface value)
    {var old=owned;brush.Surface=value;owned=value;old?.Dispose();}
    static Color Contrast(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        return .2126 * r * r + .7152 * g * g + .0722 * b * b > .20
            ? Color.FromArgb(255, 25, 28, 34) : Color.FromArgb(255, 250, 251, 252);
    }
    static Color ToColor(uint c) => Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c);
    static bool InsideRounded(FloatRect b, float x, float y)
    {
        if (!b.Contains(x, y)) return false;
        float cx = Math.Clamp(x, b.X + 24, b.Right - 24);
        return Vector2.DistanceSquared(new(x, y), new(cx, b.Y + 24)) <= 576;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; Model.Close(); Visual.Children.RemoveAll(); _face.Dispose();
        RealPageRequested=null; _satelliteBrush.Surface=_semanticBrush.Surface=null;
        _satelliteSurface?.Dispose();_semanticSurface?.Dispose();_satellite.Dispose();_semantic.Dispose();_satelliteBrush.Dispose();_semanticBrush.Dispose();
        for (int i = 0; i < 5; i++) { foreach (var s in _bars[i].Shapes) s.Dispose(); _bars[i].Shapes.Clear(); _bars[i].Dispose(); _geometry[i].Dispose(); _ink[i].Dispose(); }
        foreach (var s in _body.Shapes) s.Dispose(); _body.Shapes.Clear(); _body.Dispose(); _bodyGeometry.Dispose(); _bodyInk.Dispose(); Visual.Dispose();
    }
}
