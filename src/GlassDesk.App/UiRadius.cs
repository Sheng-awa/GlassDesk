namespace GlassDesk.App;

/// <summary>
/// The shared corner-radius scale from the final UI plan: one shell radius,
/// one card radius and one control radius. Buttons, switches and name plates
/// stay fully round and use <see cref="Full"/> with their own height.
/// </summary>
internal static class UiRadius
{
    /// <summary>Outer shells, settings pane, settings ribbon frame and dock.</summary>
    public const double Shell = 24;

    /// <summary>Cards, covers, menu flyouts and preview stages.</summary>
    public const double Card = 16;

    /// <summary>Inputs, drop-downs, colour wells and the colour picker.</summary>
    public const double Control = 10;

    /// <summary>Half the height of a pill-shaped control.</summary>
    public static double Full(double height) => Math.Max(0, height) / 2;
}
