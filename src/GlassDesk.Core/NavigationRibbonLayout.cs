namespace GlassDesk.Core;

/// <summary>Shared presentation and hit geometry. The selected item always settles at the center.</summary>
public static class NavigationRibbonLayout
{
    public const float CollapsedWidth = 48, ExpandedWidth = 264, Height = 48, Spacing = 38;
    public static float ItemOffset(int index, double centerIndex)
    {
        double offset = index - centerIndex;
        // Five slots wrap around the current item so neither end leaves empty space.
        offset -= Math.Floor((offset + 2.5) / 5) * 5;
        return (float)offset * Spacing;
    }
    public static float Width(double expansion) => CollapsedWidth + (ExpandedWidth - CollapsedWidth) * (float)expansion;
    public static int Hit(float localX, double centerIndex)
    {
        int nearest = 0; float distance = float.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            float d = Math.Abs(localX - ItemOffset(i, centerIndex));
            if (d < distance) { distance = d; nearest = i; }
        }
        return nearest;
    }
}
