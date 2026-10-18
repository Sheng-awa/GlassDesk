namespace GlassDesk.Core;

public readonly record struct WidgetReadabilityDecision(bool LightInk, bool PromoteSecondary,
    bool ContrastUncertain, double PrimaryContrastEstimate, string Reason);

/// <summary>
/// Advisory ink choice from event-driven exterior samples. These estimates do
/// not measure the composed text area and must never override user transparency.
/// Final contrast acceptance still requires real composed screenshots.
/// </summary>
public static class WidgetReadabilityPolicy
{
    public static WidgetReadabilityDecision Resolve(ReadOnlySpan<uint> colorRefs, float tintOpacity,
        bool clockWhiteplate, bool previousLight, double primaryMinimumContrast = 4.5, bool hasRequiredSecondary = true)
    {
        if (colorRefs.Length != 4 || !float.IsFinite(tintOpacity) || colorRefs.Contains(uint.MaxValue)
            || primaryMinimumContrast is not (3 or 4.5))
            return new(previousLight, true, true, 0, "Background samples unavailable; retain ink family and user material settings.");
        double tint = Math.Clamp(tintOpacity, .1f, .9f);
        double plate = clockWhiteplate ? Math.Clamp((.36 - tint) / (1 - tint), 0, .22) : 0;
        var intervals = new List<(double Low, double High)>();
        foreach (uint color in colorRefs)
        {
            double r = color & 255, g = color >> 8 & 255, b = color >> 16 & 255;
            // Include a small error interval for grain, tint gradient and blur.
            // A complex background between these samples is still unproven.
            double low = Estimate(r, g, b, tint, plate, -12), high = Estimate(r, g, b, tint, plate, 12);
            intervals.Add((low, high));
        }
        double dark = MinimumContrast(InkRole.Primary, false, intervals);
        double light = MinimumContrast(InkRole.Primary, true, intervals);
        double darkSecondary = MinimumContrast(InkRole.Secondary, false, intervals);
        double lightSecondary = MinimumContrast(InkRole.Secondary, true, intervals);
        // Large clocks alone need 3:1. Any accompanying small date or weekday
        // still needs 4.5:1, including after promotion to the primary ink.
        double Score(double primary, double secondary) => Math.Min(primary / primaryMinimumContrast,
            hasRequiredSecondary ? Math.Max(primary, secondary) / 4.5 : double.PositiveInfinity);
        double darkScore = Score(dark, darkSecondary), lightScore = Score(light, lightSecondary);
        if (Math.Max(darkScore, lightScore) < 1)
            return new(lightScore > darkScore, hasRequiredSecondary, true, lightScore > darkScore ? light : dark,
                "Estimated contrast is insufficient for required text; promote ink without overriding user transparency. Composed contrast remains unverified.");
        bool useLight = darkScore >= 1 && lightScore >= 1 ? previousLight : lightScore >= 1;
        bool promote = hasRequiredSecondary && (useLight ? lightSecondary : darkSecondary) < 4.5;
        return new(useLight, promote, false, useLight ? light : dark,
            promote ? "Promote secondary text to primary ink on this material." : "Keep the semantic ink hierarchy.");
    }

    static double Estimate(double r, double g, double b, double tint, double plate, double error)
    {
        double Channel(double c) => Math.Clamp(((c * (1 - tint) + 250 * tint) * (1 - plate) + 255 * plate + error) / 255, 0, 1);
        return .2126 * Linear(Channel(r)) + .7152 * Linear(Channel(g)) + .0722 * Linear(Channel(b));
    }
    static double MinimumContrast(InkRole role, bool light, List<(double Low, double High)> intervals)
    {
        uint ink = TypographyCatalog.Ink(role, light);
        double luminance = .2126 * Linear((ink >> 16 & 255) / 255d) + .7152 * Linear((ink >> 8 & 255) / 255d) + .0722 * Linear((ink & 255) / 255d);
        return intervals.Min(range => luminance >= range.Low && luminance <= range.High ? 1 :
            luminance < range.Low ? (range.Low + .05) / (luminance + .05) : (luminance + .05) / (range.High + .05));
    }
    static double Linear(double value) => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
}
