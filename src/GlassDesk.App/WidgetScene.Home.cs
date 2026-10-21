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

    readonly Dictionary<int, HomeAction> _homeActions = new();
    bool _homeNavExpanded, _homeHoverNav;
    int _homeHoverFolder = -1;
    int _homePopoverFolder = -1;
    int _homePressedAction = -1;
    HomeFolderPreview? _homePreview;

    internal bool HomeNavExpanded => _homeNavExpanded;
    internal bool HomePopoverOpen => _homePopoverFolder >= 0;
    internal int HomePopoverFolder => _homePopoverFolder;
    internal HomeFolderPreview? HomeFolderPreview => _homePreview;
    internal IReadOnlyDictionary<int, HomeAction> HomeActions => _homeActions;

    /// <summary>Pins the navigation entry row; hover still reveals it temporarily.</summary>
    internal void SetHomeNavExpanded(bool expanded)
    {
        if (_homeNavExpanded == expanded) return;
        _homeNavExpanded = expanded;
        _drawingInvalidated = true;
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
        _drawingInvalidated = true;
        BuildControls();
        return true;
    }

    internal bool ClearHomeHover()
    {
        if (!_homeHoverNav && _homeHoverFolder < 0) return false;
        _homeHoverNav = false;
        _homeHoverFolder = -1;
        _drawingInvalidated = true;
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
        _drawingInvalidated = true;
    }

    internal void OpenHomePopover(int index, HomeFolderPreview preview)
    {
        _homePopoverFolder = index;
        _homePreview = preview;
        _homeHoverNav = false;
        _homePressedAction = -1;
        _drawingInvalidated = true;
        BuildControls();
    }

    internal void CloseHomePopover()
    {
        if (_homePopoverFolder < 0) return;
        _homePopoverFolder = -1;
        _homePreview = null;
        _drawingInvalidated = true;
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
        var size = LayoutSize(WidgetForm.Details);
        uint cardFill = _accessibility.HighContrast ? 0x30ffffffu : 0x16ffffffu;
        uint cardInner = _accessibility.HighContrast ? 0x22ffffffu : 0x0affffffu;
        uint rim = _accessibility.HighContrast ? 0x80ffffffu : 0x24ffffffu;

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

        // Navigation card: quiet frosted slab in rest, stronger arcs when hovered/pinned.
        var nav = HomeLayout.NavCard;
        bool expanded = _homeNavExpanded || _homeHoverNav;
        RoundRect(operations, nav.X, nav.Y, nav.Width, nav.Height, 26, cardFill);
        RoundRect(operations, nav.X + 1, nav.Y + 1, nav.Width - 2, nav.Height - 2, 25, cardInner);
        if (expanded)
        {
            for (int index = 0; index < 3; index++)
            {
                var entry = HomeLayout.NavEntry(index);
                bool pressed = _homePressedAction == WidgetActions.HomeNavFirst + index;
                RoundRect(operations, entry.X, entry.Y, entry.Width, entry.Height, 12, pressed ? 0x38ffffffu : 0x26ffffffu);
                Icon(operations, HomeNavGlyphs[index], entry.X + (entry.Width - 20) / 2, entry.Y + (entry.Height - 20) / 2, 20, 0xffffffffu);
            }
            RoundRect(operations, nav.Right - 24, nav.Y + 12, 12, 12, 4, 0x1affffffu);
        }
        DrawHomeEyes(operations, nav, expanded);

        // Folder-only dock: the five-slot chrome dock is hidden on this page.
        var dock = HomeLayout.Dock;
        RoundRect(operations, dock.X, dock.Y, dock.Width, dock.Height, 20, cardFill);
        RoundRect(operations, dock.X + 1, dock.Y + 1, dock.Width - 2, dock.Height - 2, 19, cardInner);
        for (int index = 0; index < HomeLayout.FolderCenters.Count; index++)
        {
            var slot = HomeLayout.FolderSlot(index);
            bool open = _homeHoverFolder == index || _homePopoverFolder == index;
            bool pressed = _homePressedAction == WidgetActions.HomeFolderFirst + index;
            if (pressed) RoundRect(operations, slot.X, slot.Y, slot.Width, slot.Height, 12, 0x22ffffffu);
            DrawMiniFolder(operations, slot, open);
        }

        DrawHomePopover(operations);
    }

    void DrawHomeEyes(List<SurfaceOp> operations, FloatRect nav, bool expanded)
    {
        float gap = 6, eyeWidth = 6, eyeHeight = 9;
        // In the expanded state the entry row owns the bottom edge, so the eyes let go to
        // the top-right corner and stay fully visible instead of sitting on the icons.
        float x = expanded ? nav.Right - 30 : nav.X + nav.Width / 2 - (eyeWidth * 2 + gap) / 2;
        float y = expanded ? nav.Y + 12 : nav.Y + nav.Height / 2 - eyeHeight / 2;
        uint ink = 0xffffffffu;
        RoundRect(operations, x, y, eyeWidth, eyeHeight, eyeWidth / 2, ink);
        RoundRect(operations, x + eyeWidth + gap, y, eyeWidth, eyeHeight, eyeWidth / 2, ink);
    }

    static void DrawMiniFolder(List<SurfaceOp> operations, FloatRect slot, bool open)
    {
        float x = slot.X + 5, y = slot.Y + 6;
        // Back shell + tab, then the paper stack and the amber front cover.
        RoundRect(operations, x, y + 2, 34, 22, 6, 0xffb45309);
        RoundRect(operations, x, y - 1, 14, 6, 3, 0xffb45309);
        if (open)
        {
            RoundRect(operations, x + 4, y + 5, 26, 10, 4, 0xfffafafa);
            RoundRect(operations, x + 6, y + 2, 22, 9, 4, 0xffe4e4e7);
            RoundRect(operations, x + 8, y - 1, 18, 8, 4, 0xfff4f4f5);
            RoundRect(operations, x, y + 17, 34, 12, 5, 0xfffbbf24);
        }
        else
        {
            RoundRect(operations, x, y + 8, 34, 16, 5, 0xfff59e0b);
            RoundRect(operations, x, y + 14, 34, 10, 5, 0xfffbbf24);
        }
    }

    void DrawHomePopover(List<SurfaceOp> operations)
    {
        if (_homePopoverFolder < 0 || _homePreview is null) return;
        var pop = HomeLayout.Popover;
        uint fill = _accessibility.HighContrast ? 0xff000000u : 0xf2141920u;
        RoundRect(operations, pop.X, pop.Y, pop.Width, pop.Height, 20, fill);
        RoundRect(operations, pop.X + 1, pop.Y + 1, pop.Width - 2, pop.Height - 2, 19, 0x0affffffu);
        // Small source notch under the popover, tracking the triggering folder.
        float arrowX = HomeLayout.PopoverArrowX(_homePopoverFolder);
        RoundRect(operations, pop.X + arrowX - 5, pop.Bottom - 3, 10, 8, 3, fill);

        var title = HomeLayout.PopoverTitle;
        HomeText(operations, _homePreview.Name, title, TextRole.Label, 0xfff8fafc, center: false);

        for (int index = 0; index < 3; index++)
        {
            var item = HomeLayout.PopoverPreview(index);
            RoundRect(operations, item.X, item.Y, item.Width, item.Height, 12, 0x12ffffffu);
            if (index >= _homePreview.Entries.Count) continue;
            var entry = _homePreview.Entries[index];
            float docX = item.X + item.Width / 2 - 12;
            if (entry.IsFolder)
            {
                RoundRect(operations, docX, item.Y + 12, 24, 18, 4, 0xfffbbf24);
                RoundRect(operations, docX, item.Y + 9, 10, 5, 2, 0xfffbbf24);
            }
            else
            {
                RoundRect(operations, docX, item.Y + 10, 24, 30, 3, 0xfff8fafc);
                RoundRect(operations, docX + 4, item.Y + 32, 16, 4, 2, 0xffef4444);
            }
            string name = Shorten(entry.Name, 12);
            HomeText(operations, name, new FloatRect(item.X + 4, item.Y + 48, item.Width - 8, 32),
                TextRole.Caption, 0xffb4c0cc);
            if (_homePressedAction == WidgetActions.HomePreviewFirst + index)
                RoundRect(operations, item.X, item.Y, item.Width, item.Height, 12, 0x22ffffffu);
        }

        var open = HomeLayout.PopoverOpenButton;
        RoundRect(operations, open.X, open.Y, open.Width, open.Height, 12,
            _homePressedAction == WidgetActions.HomeOpenFolder ? 0x30ffffffu : 0x1affffffu);
        HomeText(operations, "打开文件夹", new FloatRect(open.X, open.Y + 12, open.Width, 20),
            TextRole.Label, 0xfff8fafc);
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
