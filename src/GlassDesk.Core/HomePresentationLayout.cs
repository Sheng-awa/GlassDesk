namespace GlassDesk.Core;

/// <summary>Viewport-aware Home geometry. The dock stays fixed; only the grid scrolls.</summary>
public sealed record HomePresentationLayout(float Width, float Height, float Scroll)
{
    public bool Compact => Height < 460;
    public float RowHeight => Compact ? 84 : HomeLayout.CellHeight;
    public float RowGap => Compact ? 8 : HomeLayout.RowGap;
    public float CellWidth => (Width - HomeLayout.Inset * 2 - HomeLayout.ColumnGap * 3) / 4;
    public FloatRect Dock => new(20, Height - 72, Width - 40, 56);
    public FloatRect GridViewport => new(0, 54, Width, Math.Max(44, Dock.Y - 12 - 54));
    public float ContentBottom => HomeLayout.GridTop + RowHeight * 3 + RowGap * 2;
    public float MaxScroll => Math.Max(0, ContentBottom - GridViewport.Bottom);
    public FloatRect NavCard => new(HomeLayout.Inset + 2 * (CellWidth + HomeLayout.ColumnGap),
        HomeLayout.GridTop + RowHeight + RowGap, CellWidth * 2 + HomeLayout.ColumnGap, RowHeight * 2 + RowGap);
    public static HomePresentationLayout Resolve(float width, float height, float scroll = 0)
    {
        var layout = new HomePresentationLayout(Math.Clamp(width, 320, 360), Math.Clamp(height, 300, 512), 0);
        return layout with { Scroll = float.IsFinite(scroll) ? Math.Clamp(scroll, 0, layout.MaxScroll) : 0 };
    }
    public FloatRect AppSlot(int index)
    {
        var (col, row) = HomeLayout.AppCells[index];
        return new(HomeLayout.Inset + col * (CellWidth + HomeLayout.ColumnGap),
            HomeLayout.GridTop + row * (RowHeight + RowGap), CellWidth, RowHeight);
    }
    public FloatRect AppIcon(int index)
    {
        var slot = AppSlot(index);
        return new(slot.X + (slot.Width - 52) / 2, slot.Y + (slot.Height - 72) / 2, 52, 52);
    }
    public FloatRect AppLabel(int index)
    {
        var icon = AppIcon(index);
        return new(AppSlot(index).X - 2, icon.Bottom + 4, CellWidth + 4, 16);
    }
    public FloatRect NavEntry(int index)
    {
        if (index is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(index));
        var nav = NavCard;
        float centerY = Math.Min(nav.Height * .3f + 14, nav.Height - 22 - 92) + index * 46;
        float centerX = nav.Width * HomeLayout.NavBoxScales[index] - 18;
        return new(nav.X + centerX - 22, nav.Y + centerY - 22, 44, 44);
    }
    public FloatRect FolderSlot(int index) => new(Width / 2 + (index - 1) * 56 - 22, Dock.Y + 6, 44, 44);
    public FloatRect Popover
    {
        get
        {
            float width = Math.Min(312, Width - 48), height = Math.Min(208, Dock.Y - 64);
            return new((Width - width) / 2, Dock.Y - 12 - height, width, height);
        }
    }
    public FloatRect PopoverTitle => new(Popover.X + 12, Popover.Y + 12, Popover.Width - 24, 20);
    public FloatRect PopoverOpenButton => new(Popover.X + 12, Popover.Bottom - 60, Popover.Width - 24, 44);
    public FloatRect Preview(int index)
    {
        float width = (Popover.Width - 40) / 3;
        return new(Popover.X + 12 + index * (width + 8), Popover.Y + 40, width,
            PopoverOpenButton.Y - Popover.Y - 48);
    }
    public FloatRect Presented(FloatRect rect) => rect with { Y = rect.Y - Scroll };
    public FloatRect? VisibleTarget(FloatRect rect)
    {
        rect = Presented(rect);
        float top = Math.Max(rect.Y, GridViewport.Y), bottom = Math.Min(rect.Bottom, GridViewport.Bottom);
        // DIP arithmetic can lose a few ulps when the bottom edge coincides
        // with the viewport; do not drop a fully visible 44-DIP destination.
        return bottom - top >= 44 - .001f ? new(rect.X, top, rect.Width, bottom - top) : null;
    }
}

