using System.Globalization;
using System.Numerics;
using GlassDesk.Core;
using GlassDesk.NativeBridge;
using Windows.UI.Composition;

namespace GlassDesk.App;

public sealed partial class WidgetScene
{
    string _detailsPageId = DetailsPageController.Music;
    float _detailsWidth = 360, _boardScroll;
    long _boardRevision = -1;
    WidgetBoardState _board = WidgetBoardData.CreateDefault();
    IReadOnlyList<WidgetInstanceResult> _boardResults = [];
    bool _boardEditing, _boardAddMenu;
    SpriteVisual _boardCards = null!;
    CompositionSurfaceBrush _boardBrush = null!;
    CompositionDrawingSurface? _boardSurface;
    WidgetBoardRenderResult? _boardRender;
    public bool WidgetsPage => _detailsPageId == DetailsPageController.Widgets;
    public float CurrentPageScroll => WidgetsPage ? _boardScroll : _detailsScroll;
    internal WidgetBoardRenderResult? BoardRender => _boardRender;
    public event Action<string>? DetailsPageRequested;
    public event Action? BoardViewportChanged;
    // The widget page height follows its content: one card stays compact, more cards grow
    // up to 512 and then scroll. The dock inset is part of the formula so it never overlaps.
    public float BoardNaturalHeight => Math.Clamp(
        WidgetBoardLayout.Pack(_board, _detailsWidth).ContentBottom + WidgetBoardLayout.BottomInset, 324, 512);

    void InitializeBoardVisuals()
    {
        _boardCards = _host.Compositor.CreateSpriteVisual();
        _boardBrush = _host.Compositor.CreateSurfaceBrush();
        _boardBrush.Stretch = CompositionStretch.Fill;
        _boardCards.Brush = _boardBrush;
        _boardCards.Offset = new(0, WidgetBoardLayout.Top, 0);
        _boardCards.Opacity = 0;
        // The toolbar owns transient menus and stays above the card viewport.
        _bodyRoot.Children.InsertBelow(_boardCards, _content);
        _chrome.PageRequested += id => DetailsPageRequested?.Invoke(id);
    }

    internal void SetWidgetBoard(string pageId, WidgetBoardState board, IReadOnlyList<WidgetInstanceResult> results,
        long revision, bool editing, bool addMenuOpen, float scrollOffset)
    {
        bool changed = _detailsPageId != pageId || _boardRevision != revision || _boardEditing != editing || _boardAddMenu != addMenuOpen;
        _detailsPageId = DetailsPageController.Normalize(pageId);
        _board = WidgetBoardData.TimeOnlyForPresentation(WidgetBoardData.ValidateAndClone(board)); _boardResults = results; _boardRevision = revision;
        _boardEditing = editing; _boardAddMenu = addMenuOpen;
        _boardScroll = float.IsFinite(scrollOffset) ? Math.Max(0,scrollOffset) : 0;
        if(!WidgetsPage)_detailsScroll=_boardScroll;
        _chrome.SetCurrentPage(_detailsPageId);
        if (changed) _drawingInvalidated = true;
    }

    void DrawBoard(List<SurfaceOp> operations)
    {
        var size = LayoutSize(WidgetForm.Details);
        bool firstBoardRender = _boardRender is null;
        _boardRender = WidgetBoardRenderer.Build(_board,_boardResults,size.X,size.Y,_boardScroll,
            _boardEditing,_boardAddMenu,_lightText,CultureInfo.CurrentUICulture.Name,_accessibility.HighContrast);
        _boardScroll = _boardRender.ScrollOffset;
        _boardCards.Size = new(size.X, Math.Max(1,size.Y-WidgetBoardLayout.BottomInset-WidgetBoardLayout.Top));
        ReplaceSurface(ref _boardSurface,_boardBrush,RenderTextLayer("WidgetBoard",_boardCards.Size.X,_boardCards.Size.Y,_boardRender.CardOperations));
        operations.AddRange(_boardRender.ToolbarOperations);
        // The first painted frame publishes its packing so the host can schedule providers.
        // Without it, a page opened before its first render can stay in "preparing" forever.
        if (firstBoardRender) BoardViewportChanged?.Invoke();
    }

    bool ScrollBoard(float delta)
    {
        if (_boardRender is null || _boardAddMenu) return false;
        float viewportHeight = Math.Max(0, CurrentSize.Y - WidgetBoardLayout.Top - WidgetBoardLayout.BottomInset);
        float maximum = Math.Max(0,_boardRender.ContentHeight-viewportHeight);
        float next = Math.Clamp(_boardScroll+delta,0,maximum);
        if (Math.Abs(next-_boardScroll)<.1f) return false;
        _boardScroll=next; _drawingInvalidated=true;
        Render(_form,_media,_now,_preferences,_notice,_capsuleMode);
        BoardViewportChanged?.Invoke();
        return true;
    }

    void ApplyBoardVisibility(Vector3 weights)
    {
        _boardCards.Offset = new((_presentedSize.X-_detailsWidth)/2,WidgetBoardLayout.Top,0);
        _boardCards.Opacity = WidgetsPage ? weights.Z : 0;
        if (!WidgetsPage) return;
        _detailsViewport.Opacity = _timeline.Opacity = _detailsButtons.Opacity = 0;
        _audioBars.Opacity = _preferences.AudioVisualizationEnabled
            ? Math.Clamp((MusicFor(WidgetForm.Capsule)?weights.X:0)+(_media.Connected?weights.Y:0),0,1) : 0;
        _artworkViewport.Opacity = _media.Connected
            ? Math.Clamp((MusicFor(WidgetForm.Capsule)&&!CompanionVisible?weights.X:0)+weights.Y,0,1) : 0;
    }

    void DisposeBoardVisuals()
    {
        DetailsPageRequested = null; BoardViewportChanged = null;
        _boardBrush.Surface=null; _boardSurface?.Dispose();
        _bodyRoot.Children.Remove(_boardCards); _boardCards.Dispose(); _boardBrush.Dispose();
    }
}
