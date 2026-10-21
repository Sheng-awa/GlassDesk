using System.Globalization;
using GlassDesk.Core;
using GlassDesk.NativeBridge;
using Microsoft.UI.Composition;

namespace GlassDesk.App;

internal enum HomeActionKind { App, ToggleNav, NavEntry, Folder, Preview, OpenFolder, ClosePopover }

internal readonly record struct HomeAction(HomeActionKind Kind, int Index);

internal readonly record struct HomeFolderEntry(string Name, string Path, bool IsFolder);

internal sealed record HomeFolderPreview(string Name, string Path, IReadOnlyList<HomeFolderEntry> Entries);

public sealed partial class WidgetScene
{
    static readonly string[] HomeAppNames = { "网易云音乐", "Edge", "Terminal", "照片", "记事本", "计算器" };
    static readonly string[] HomeAppGlyphs = { "\uE8D6", "\uE774", "\uE756", "\uE91B", "\uE70F", "\uE8EF" };
    static readonly uint[] HomeAppColors =
    {
        0xffe11d48, 0xff0284c7, 0xff0f172a, 0xff0d9488, 0xff059669, 0xff4338ca,
    };
    static readonly string[] HomeNavGlyphs = { "\uE8D6", "\uE71D", "\uE713" };
    static readonly string[] HomeFolderLabels = { "crucial", "项目", "下载" };

    readonly Dictionary<int, HomeAction> _homeActions = new();
    bool _homeNavExpanded, _homeHoverNav;
    int _homeHoverFolder = -1;
    int _homePopoverFolder = -1;
    int _homePressedAction = -1;
    HomeFolderPreview? _homePreview;
    bool _homeAnimating;
    double _homeLastTick;
    float _homeNavProgress, _homePopoverProgress;
    readonly float[] _homeFolderProgress = new float[3];
    float _homeGazeX, _homeGazeY;

    internal bool HomeNavExpanded => _homeNavExpanded;
    internal bool HomePopoverOpen => _homePopoverFolder >= 0;
    internal int HomePopoverFolder => _homePopoverFolder;
    internal HomeFolderPreview? HomeFolderPreview => _homePreview;
    internal IReadOnlyDictionary<int, HomeAction> HomeActions => _homeActions;
    internal bool HomeAnimationActive => _homeAnimating;

    void MarkHomeAnimation()
    {
        _homeAnimating = true;
        _homeLastTick = -1;
        _drawingInvalidated = true;
    }

    /// <summary>Eases the home reveal/progress values; <paramref name="now"/> is milliseconds.
    /// Returns true when a surface repaint is needed.</summary>
    internal bool TickHome(double now)
    {
        if (!_homeAnimating) return false;
        if (_form != WidgetForm.Details) { _homeAnimating = false; return false; }
        if (_homeLastTick <= 0 || now < _homeLastTick) { _homeLastTick = now; return false; }
        float dt = (float)Math.Clamp(now - _homeLastTick, 0, 50);
        _homeLastTick = now;
        bool moving = false;
        moving |= Advance(ref _homeNavProgress, (_homeNavExpanded || _homeHoverNav) ? 1 : 0, dt / 260f);
        for (int index = 0; index < _homeFolderProgress.Length; index++)
            moving |= Advance(ref _homeFolderProgress[index],
                _homeHoverFolder == index || _homePopoverFolder == index ? 1 : 0, dt / 300f);
        moving |= Advance(ref _homePopoverProgress, _homePopoverFolder >= 0 ? 1 : 0, dt / 220f);
        if (moving) _drawingInvalidated = true;
        else _homeAnimating = false;
        return moving;
    }

    static bool Advance(ref float value, float target, float step)
    {
        if (step <= 0) return false;
        float next = target > value ? Math.Min(target, value + step) : Math.Max(target, value - step);
        if (Math.Abs(next - value) < .0005f) { value = target; return false; }
        value = next;
        return true;
    }

    static float Ease(float value) => value * value * (3 - 2 * value);

    static uint Fade(uint color, float opacity) =>
        (color & 0x00ffffffu) | ((uint)Math.Clamp((int)Math.Round(opacity * 255), 0, 255) << 24);

    /// <summary>Pins the navigation entry row; hover still reveals it temporarily.</summary>
    internal void SetHomeNavExpanded(bool expanded)
    {
        if (_homeNavExpanded == expanded) return;
        _homeNavExpanded = expanded;
        MarkHomeAnimation();
        BuildControls();
    }

    /// <summary>Hover state for the navigation card and dock folders. Returns true when a repaint is needed.</summary>
    internal bool UpdateHomePointer(float x, float y)
    {
        if (!HomePage) return false;
        bool nav = HomeLayout.NavCardHit.Contains(x, y);
        int folder = -1;
        if (_homePopoverFolder < 0)
            for (int index = 0; index < HomeLayout.FolderCenters.Count; index++)
                if (HomeLayout.FolderSlot(index).Contains(x, y)) { folder = index; break; }
        if (nav == _homeHoverNav && folder == _homeHoverFolder) return false;
        _homeHoverNav = nav;
        _homeHoverFolder = folder;
        var card = HomeLayout.NavCard;
        _homeGazeX = Math.Clamp((x - (card.X + card.Width / 2)) * .06f, -2.4f, 2.4f);
        _homeGazeY = Math.Clamp((y - (card.Y + card.Height / 2)) * .06f, -2.4f, 2.4f);
        MarkHomeAnimation();
        BuildControls();
        return true;
    }

    internal bool ClearHomeHover()
    {
        if (!_homeHoverNav && _homeHoverFolder < 0) return false;
        _homeHoverNav = false;
        _homeHoverFolder = -1;
        _homeGazeX = _homeGazeY = 0;
        MarkHomeAnimation();
        BuildControls();
        return true;
    }

    /// <summary>Leaving Home drops transient hover/popover state; the pinned entry row is reset too.</summary>
    internal void ResetHomeState()
    {
        if (!_homeNavExpanded && !_homeHoverNav && _homeHoverFolder < 0 && _homePopoverFolder < 0) return;
        _homeNavExpanded = false;
        _homeHoverNav = false;
        _homeHoverFolder = -1;
        _homePopoverFolder = -1;
        _homePreview = null;
        _homePressedAction = -1;
        _homeGazeX = _homeGazeY = 0;
        _homeNavProgress = _homePopoverProgress = 0;
        for (int index = 0; index < _homeFolderProgress.Length; index++) _homeFolderProgress[index] = 0;
        _homeAnimating = false;
        _drawingInvalidated = true;
    }

    internal void OpenHomePopover(int index, HomeFolderPreview preview)
    {
        _homePopoverFolder = index;
        _homePreview = preview;
        _homeHoverNav = false;
        _homePressedAction = -1;
        MarkHomeAnimation();
        BuildControls();
    }

    internal void CloseHomePopover()
    {
        if (_homePopoverFolder < 0) return;
        _homePopoverFolder = -1;
        _homePreview = null;
        MarkHomeAnimation();
        BuildControls();
    }

    internal void SetHomePointerFeedback(int actionId)
    {
        if (_homePressedAction == actionId) return;
        _homePressedAction = actionId;
        _drawingInvalidated = true;
    }

    static void HomeText(List<SurfaceOp> ops, string value, FloatRect rect, TextRole role, uint color, bool center = true)
        => ops.Add(SurfaceOp.Typography(value, role, rect.X, rect.Y, rect.Width, rect.Height, color, center, null,
            CultureInfo.CurrentUICulture.Name));

    void DrawHome(List<SurfaceOp> operations)
    {
        uint cardFill = _accessibility.HighContrast ? 0x30ffffffu : 0x16ffffffu;
        uint cardInner = _accessibility.HighContrast ? 0x22ffffffu : 0x0affffffu;
        float navP = Ease(_homeNavProgress);

        // External application shortcuts: icon + Caption 12/16 label, no container frame.
        for (int index = 0; index < HomeAppNames.Length; index++)
        {
            var (column, row) = HomeLayout.AppCells[index];
            var cell = HomeLayout.AppSlot(column, row);
            var icon = HomeLayout.AppIcon(column, row);
            var label = HomeLayout.AppLabel(column, row);
            RoundRect(operations, icon.X, icon.Y, icon.Width, icon.Height, HomeLayout.IconSize / 2 - 12, HomeAppColors[index]);
            Icon(operations, HomeAppGlyphs[index], icon.X + (icon.Width - 24) / 2, icon.Y + (icon.Height - 24) / 2, 24, 0xffffffffu);
            HomeText(operations, HomeAppNames[index], label, TextRole.Caption, 0xffb4c0cc);
            if (_homePressedAction == WidgetActions.HomeAppFirst + index)
                RoundRect(operations, cell.X + 4, cell.Y + 4, cell.Width - 8, cell.Height - 8, 14, 0x1affffffu);
        }

        // Navigation card: the reference scales the card slightly and slides four nested
        // glass boxes out of the bottom-left corner (70/50/30/10%, 0.2s stagger each).
        var nav = HomeLayout.NavCard;
        float inflate = 2f * navP;
        RoundRect(operations, nav.X - inflate, nav.Y - inflate, nav.Width + inflate * 2,
            nav.Height + inflate * 2, 26 + inflate * .5f, cardFill);
        RoundRect(operations, nav.X - inflate + 1, nav.Y - inflate + 1,
            nav.Width + inflate * 2 - 2, nav.Height + inflate * 2 - 2, 25, cardInner);
        Rect(operations, nav.X + 8, nav.Y - inflate + 1.5f, nav.Width - 16, 1.2f,
            Fade(0xffffffu, .16f));
        for (int index = 0; index < 4; index++)
        {
            float delay = index * .11f;
            float local = Ease(Math.Clamp((navP - delay) / Math.Max(.05f, 1 - delay), 0, 1));
            if (local <= .012f) continue;
            var box = HomeLayout.NavBox(index);
            float slideX = -(box.Width + 8) * (1 - local) - 4;
            float slideY = -(box.Height + 8) * (1 - local) - 4;
            float x = box.X + slideX, y = box.Y + slideY;
            bool pressed = index < 3 && _homePressedAction == WidgetActions.HomeNavFirst + index;
            float boxOpacity = index switch { 0 => .26f, 1 => .22f, 2 => .20f, _ => .10f };
            RoundRect(operations, x, y, box.Width, box.Height, 10 - index,
                Fade(pressed ? 0x4affffffu : 0xffffffu, (pressed ? .34f : boxOpacity) * local));
            Rect(operations, x + 6, y + 1, box.Width - 12, 1.2f, Fade(0xffffffu, .34f * local));
            if (index < 3)
                Icon(operations, HomeNavGlyphs[index], x + box.Width - 28, y + 8, 18, Fade(0xffffffffu, .92f * local));
        }
        if (navP > .012f)
        {
            HomeText(operations, "导航", new FloatRect(nav.X + 10, nav.Bottom - 22, 60, 16),
                TextRole.Caption, Fade(0xffffffffu, .55f * navP), center: false);
            RoundRect(operations, nav.Right - 24 + inflate, nav.Y + 12 - inflate, 12, 12, 4,
                Fade(0xffffffffu, .10f * navP));
        }
        DrawHomeEyes(operations, nav, navP);

        // Folder-only dock: the five-slot chrome dock is hidden on this page.
        var dock = HomeLayout.Dock;
        RoundRect(operations, dock.X, dock.Y, dock.Width, dock.Height, 20, cardFill);
        RoundRect(operations, dock.X + 1, dock.Y + 1, dock.Width - 2, dock.Height - 2, 19, cardInner);
        Rect(operations, dock.X + 10, dock.Y + 1.5f, dock.Width - 20, 1.2f, Fade(0xffffffu, .26f));
        for (int index = 0; index < HomeLayout.FolderCenters.Count; index++)
        {
            var slot = HomeLayout.FolderSlot(index);
            float folderP = Ease(_homeFolderProgress[index]);
            DrawMiniFolder(operations, slot, folderP,
                _homePressedAction == WidgetActions.HomeFolderFirst + index);
            if (folderP > .15f)
            {
                float labelAlpha = Math.Clamp((folderP - .15f) / .85f, 0, 1);
                var label = new FloatRect(slot.X - 16, dock.Y - 30 - (1 - labelAlpha) * 3, slot.Width + 32, 20);
                RoundRect(operations, label.X, label.Y, label.Width, label.Height, 7, Fade(0x1e2833u, .82f * labelAlpha));
                HomeText(operations, HomeFolderLabels[index], label, TextRole.Caption, Fade(0xf8fafcu, .95f * labelAlpha));
            }
        }

        DrawHomePopover(operations);
    }

    void DrawHomeEyes(List<SurfaceOp> operations, FloatRect nav, float navP)
    {
        float gap = 6, eyeWidth = 6, eyeHeight = 9;
        float startX = nav.X + nav.Width / 2 - (eyeWidth * 2 + gap) / 2;
        float startY = nav.Y + nav.Height / 2 - eyeHeight / 2;
        float endX = nav.Right - 28;
        float endY = nav.Bottom - 21;
        float x = startX + (endX - startX) * navP + _homeGazeX;
        float y = startY + (endY - startY) * navP + _homeGazeY;
        uint ink = 0xffffffffu;
        RoundRect(operations, x, y, eyeWidth, eyeHeight, eyeWidth / 2, ink);
        RoundRect(operations, x + eyeWidth + gap, y, eyeWidth, eyeHeight, eyeWidth / 2, ink);
    }

    /// <summary>Five-layer folder from the mighty-pig-13 reference at dock scale: amber-600
    /// back with top-left tab, three zinc papers tilting around the bottom edge, and the
    /// amber-500 front cover with its top-right tab. Two protruding tabs keep the folder
    /// silhouette readable even at 42 DIP.</summary>
    static void DrawMiniFolder(List<SurfaceOp> operations, FloatRect slot, float open, bool pressed)
    {
        float p = Math.Clamp(open, 0, 1);
        float width = 42 + 2 * p;
        float height = 29 + 1.5f * p;
        float x = slot.X + (slot.Width - width) / 2;
        float bottom = slot.Y + slot.Height - 3 - 2 * p;
        float top = bottom - height;
        // Contact shadow and hover surface.
        RoundRect(operations, x + 3, bottom + 1, width - 6, 3, 2, Fade(0x000000u, .22f));
        if (p > .01f) RoundRect(operations, slot.X, slot.Y, slot.Width, slot.Height, 12, Fade(0xffffffu, .07f * p));
        if (pressed) RoundRect(operations, slot.X, slot.Y, slot.Width, slot.Height, 12, 0x22ffffffu);
        // Back shell (amber-600) and its top-left tab, both protruding above the body.
        RoundRect(operations, x + 2, top - 3, 15, 7, 3, 0xffd97706);
        RoundRect(operations, x, top + 2, width, height - 2, 5, 0xffd97706);
        // Papers: zinc-400/300/200 tilt back around the bottom edge and fan above the cover.
        DrawPaper(0xffa1a1aau, .94f, 1.1f);
        DrawPaper(0xffd4d4d8u, .87f, 1.7f);
        DrawPaper(0xffe4e4e7u, .79f, 2.3f);
        // Amber front cover with a small gradient (amber-500 to amber-400) and top-right tab.
        float coverHeight = (height - 2) * (1 - (1 - .68f) * p);
        float coverTop = bottom - coverHeight;
        RoundRect(operations, x + 1, coverTop, width - 2, coverHeight, 5, 0xfff59e0b);
        Rect(operations, x + 3, coverTop + 1, width - 6, 1.2f, Fade(0xfffbbf24u, .9f));
        RoundRect(operations, x + width - 22, coverTop - 3, 22, 6, 3, 0xfffbbf24);
        Rect(operations, x + 3, coverTop + 3.5f, width - 6, 1, Fade(0xffd97706u, .35f));
        if (p > .01f)
            Rect(operations, x + 2, coverTop + 1, width - 4, 1.2f, Fade(0xfffde68au, .8f * p));

        void DrawPaper(uint color, float tilt, float inset)
        {
            float visible = (height - 2) * (1 - (1 - tilt) * p);
            RoundRect(operations, x + inset, bottom - visible, width - inset * 2, visible, 3, color);
        }
    }

    void DrawHomePopover(List<SurfaceOp> operations)
    {
        if (_homePopoverFolder < 0 || _homePreview is null) return;
        float p = Ease(_homePopoverProgress);
        if (p <= .012f) return;
        float dy = (1 - p) * 6;
        var baseRect = HomeLayout.Popover;
        var pop = new FloatRect(baseRect.X, baseRect.Y + dy, baseRect.Width, baseRect.Height);
        uint fill = _accessibility.HighContrast ? 0xff000000u : 0xf2141920u;
        RoundRect(operations, pop.X, pop.Y, pop.Width, pop.Height, 20, Fade(fill, p));
        RoundRect(operations, pop.X + 1, pop.Y + 1, pop.Width - 2, pop.Height - 2, 19, Fade(0xffffffu, .04f * p));
        // Small source notch under the popover, tracking the triggering folder.
        float arrowX = HomeLayout.PopoverArrowX(_homePopoverFolder);
        RoundRect(operations, pop.X + arrowX - 5, pop.Bottom - 3, 10, 8, 3, Fade(fill, p));

        var titleBase = HomeLayout.PopoverTitle;
        var title = new FloatRect(titleBase.X, titleBase.Y + dy, titleBase.Width, titleBase.Height);
        HomeText(operations, _homePreview.Name, title, TextRole.Label, Fade(0xfff8fafcu, p), center: false);

        for (int index = 0; index < 3; index++)
        {
            var itemBase = HomeLayout.PopoverPreview(index);
            var item = new FloatRect(itemBase.X, itemBase.Y + dy, itemBase.Width, itemBase.Height);
            RoundRect(operations, item.X, item.Y, item.Width, item.Height, 12, Fade(0xffffffu, .07f * p));
            if (index >= _homePreview.Entries.Count) continue;
            var entry = _homePreview.Entries[index];
            float docX = item.X + item.Width / 2 - 12;
            if (entry.IsFolder)
            {
                RoundRect(operations, docX, item.Y + 12, 24, 18, 4, Fade(0xfffbbf24u, p));
                RoundRect(operations, docX, item.Y + 9, 10, 5, 2, Fade(0xfffbbf24u, p));
            }
            else
            {
                RoundRect(operations, docX, item.Y + 10, 24, 30, 3, Fade(0xfff8fafcu, p));
                RoundRect(operations, docX + 4, item.Y + 32, 16, 4, 2, Fade(0xffef4444u, p));
            }
            string name = Shorten(entry.Name, 12);
            HomeText(operations, name, new FloatRect(item.X + 4, item.Y + 48, item.Width - 8, 32),
                TextRole.Caption, Fade(0xffb4c0ccu, p));
            if (_homePressedAction == WidgetActions.HomePreviewFirst + index)
                RoundRect(operations, item.X, item.Y, item.Width, item.Height, 12, Fade(0xffffffu, .13f * p));
        }

        var openBase = HomeLayout.PopoverOpenButton;
        var open = new FloatRect(openBase.X, openBase.Y + dy, openBase.Width, openBase.Height);
        RoundRect(operations, open.X, open.Y, open.Width, open.Height, 12,
            Fade(0xffffffu, _homePressedAction == WidgetActions.HomeOpenFolder ? .19f : .10f * p));
        HomeText(operations, "打开文件夹", new FloatRect(open.X, open.Y + 12, open.Width, 20),
            TextRole.Label, Fade(0xfff8fafcu, p));
    }

    static string Shorten(string value, int limit) =>
        value.Length <= limit ? value : value[..Math.Max(1, limit - 1)] + "…";

    void AddHomeControls(List<AccessibleControl> controls)
    {
        _homeActions.Clear();
        void Add(int id, string name, FloatRect rect, HomeAction action)
        {
            controls.Add(new AccessibleControl(id, name, rect.X, rect.Y, rect.Width, rect.Height));
            _homeActions[id] = action;
        }

        for (int index = 0; index < HomeAppNames.Length; index++)
        {
            var (column, row) = HomeLayout.AppCells[index];
            Add(WidgetActions.HomeAppFirst + index, "启动 " + HomeAppNames[index],
                HomeLayout.AppSlot(column, row), new HomeAction(HomeActionKind.App, index));
        }

        var nav = HomeLayout.NavCard;
        Add(WidgetActions.HomeNavToggle, _homeNavExpanded ? "收起导航" : "展开导航", nav,
            new HomeAction(HomeActionKind.ToggleNav, 0));
        if (_homeNavExpanded || _homeHoverNav)
            for (int index = 0; index < 3; index++)
                Add(WidgetActions.HomeNavFirst + index,
                    index == 0 ? "音乐" : index == 1 ? "小组件" : "设置",
                    HomeLayout.NavEntry(index), new HomeAction(HomeActionKind.NavEntry, index));

        for (int index = 0; index < HomeLayout.FolderCenters.Count; index++)
        {
            string name = _homePreview is not null && index == _homePopoverFolder
                ? _homePreview.Name
                : index == 0 ? "crucial" : index == 1 ? "项目" : "下载";
            Add(WidgetActions.HomeFolderFirst + index, name + " 文件夹",
                HomeLayout.FolderSlot(index), new HomeAction(HomeActionKind.Folder, index));
        }

        if (_homePopoverFolder >= 0 && _homePreview is not null)
        {
            Add(WidgetActions.HomeClosePopover, "文件夹内容", HomeLayout.Popover,
                new HomeAction(HomeActionKind.ClosePopover, 0));
            for (int index = 0; index < Math.Min(3, _homePreview.Entries.Count); index++)
                Add(WidgetActions.HomePreviewFirst + index, _homePreview.Entries[index].Name,
                    HomeLayout.PopoverPreview(index), new HomeAction(HomeActionKind.Preview, index));
            Add(WidgetActions.HomeOpenFolder, "打开文件夹", HomeLayout.PopoverOpenButton,
                new HomeAction(HomeActionKind.OpenFolder, 0));
        }
    }
}
