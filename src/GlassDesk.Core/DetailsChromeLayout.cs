using System.Numerics;

namespace GlassDesk.Core;

/// <summary>
/// Frozen geometry (DIP, origin top-left) for the Details chrome: the companion home
/// handle on top and the five-destination floating dock on the bottom. Details cards
/// are 360 wide; both elements are centred horizontally and the dock keeps a 16 DIP
/// bottom inset so the card corner stays visible on every page height.
/// </summary>
public static class DetailsChromeLayout
{
    public const float CardWidth = 360;
    public const float HomeWidth = 58, HomeHeight = 28, HomeTop = 14, HomeRadius = 14;
    public const float HomeHitSize = 44;
    public const float DockWidth = 320, DockHeight = 48, DockBottom = 16, DockRadius = 24;
    // Five equal slots inside the 320 DIP dock: item centres are 32/96/160/224/288.
    public const float DockItemWidth = DockWidth / 5;
    public const float DockItemHitSize = 44;
    public const float DockActiveWidth = 36, DockActiveHeight = 36, DockActiveRadius = 18;
    public const float LabelHeight = 20, LabelRadius = 10, LabelGap = 4, HomeLabelGap = 2;

    public static FloatRect Home(Vector2 size) => new((size.X - HomeWidth) / 2, HomeTop, HomeWidth, HomeHeight);

    /// <summary>The visible handle is small but the pointer target is at least 44×44.</summary>
    public static FloatRect HomeHit(Vector2 size)
    {
        FloatRect home = Home(size);
        return new(home.X + (home.Width - HomeHitSize) / 2, home.Y + (home.Height - HomeHitSize) / 2,
            HomeHitSize, HomeHitSize);
    }

    public static FloatRect Dock(Vector2 size) =>
        new((size.X - DockWidth) / 2, size.Y - DockBottom - DockHeight, DockWidth, DockHeight);

    public static float DockItemCenter(Vector2 size, int index) =>
        Dock(size).X + DockItemWidth * (index + .5f);

    public static FloatRect DockItem(Vector2 size, int index)
    {
        float center = DockItemCenter(size, index);
        return new(center - DockItemHitSize / 2, Dock(size).Y, DockItemHitSize, DockHeight);
    }

    /// <summary>White selection capsule, vertically centred inside the dock.</summary>
    public static FloatRect DockActive(Vector2 size, int index)
    {
        float center = DockItemCenter(size, index);
        return new(center - DockActiveWidth / 2, Dock(size).Y + (DockHeight - DockActiveHeight) / 2,
            DockActiveWidth, DockActiveHeight);
    }

    /// <summary>20 DIP nameplate above the dock item; clamped inside the card.</summary>
    public static FloatRect DockLabel(Vector2 size, int index, float labelWidth) =>
        Label(size, DockItemCenter(size, index), Dock(size).Y - LabelGap - LabelHeight, labelWidth);

    /// <summary>20 DIP nameplate below the home handle; clamped inside the card.</summary>
    public static FloatRect HomeLabel(Vector2 size, float labelWidth)
    {
        FloatRect home = Home(size);
        return Label(size, home.X + home.Width / 2, home.Bottom + HomeLabelGap, labelWidth);
    }

    static FloatRect Label(Vector2 size, float centerX, float y, float width)
    {
        width = Math.Clamp(float.IsFinite(width) ? width : LabelHeight, 1, Math.Max(1, size.X - 8));
        float x = Math.Clamp(centerX - width / 2, 4, Math.Max(4, size.X - 4 - width));
        return new(x, y, width, LabelHeight);
    }
}
