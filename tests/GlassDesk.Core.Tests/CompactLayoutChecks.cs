using System.Numerics;
using GlassDesk.Core;

/// <summary>Responsive Details/music geometry: the frozen 476 DIP baseline, compact short
/// heights, the pinned transport/timeline/dock stack, and the single scrolling body region.</summary>
static class CompactLayoutChecks
{
    public static void Run(Action<bool, string> check)
    {
        static bool Near(float a, float b) => Math.Abs(a - b) < .001f;
        static DetailsResponsiveResult Resolve(float width, float height, bool timeline, float plugin = 0) =>
            DetailsResponsiveLayout.Resolve(new DetailsResponsiveInput(new Vector2(width, height), timeline, plugin));
        static bool Throws(Action action)
        {
            try { action(); return false; } catch (ArgumentException) { return true; }
        }

        var baseline = Resolve(360, 476, true);

        check(DetailsResponsiveLayout.PreferredHeight == 476 && !baseline.Compact && baseline.HasTimeline
            && baseline.Home == new FloatRect(151, 14, 58, 28)
            && baseline.Dock == new FloatRect(20, 412, 320, 48)
            && baseline.Cover == new FloatRect(96, 70, 168, 168) && baseline.CoverRadius == 16
            && baseline.Title == new FloatRect(24, 246, 312, 24)
            && baseline.Artist == new FloatRect(24, 274, 312, 20)
            && baseline.Timeline == new FloatRect(56, 300, 248, 38)
            && baseline.PreviousButton == new FloatRect(96, 346, 44, 44)
            && baseline.ToggleButton == new FloatRect(152, 340, 56, 56)
            && baseline.NextButton == new FloatRect(220, 346, 44, 44),
            "476 DIP music page keeps every frozen Details chrome/cover/title/timeline/transport rect");

        check(!Resolve(360, 476, true).Compact && !Resolve(360, 512, true).Compact
            && Resolve(360, 440, true).Compact && Resolve(360, 400, true).Compact
            && Resolve(360, 360, true).Compact && Resolve(360, 300, true).Compact,
            "compact mode engages below 476 DIP and stays off at or above it");

        bool transportPinned = true, transportInside = true, transportSizes = true;
        foreach (float height in new[] { 360f, 400f, 440f, 476f, 512f })
        {
            var layout = Resolve(360, height, true);
            transportPinned &= Near(layout.ToggleButton.Bottom, layout.Dock.Y - 16)
                && Near(layout.PreviousButton.Bottom, layout.Dock.Y - 16 - 6)
                && Near(layout.NextButton.Bottom, layout.Dock.Y - 16 - 6);
            transportInside &= layout.Home.Y >= 0 && layout.Home.Bottom <= height
                && layout.ToggleButton.X >= 0 && layout.NextButton.Right <= 360
                && layout.ToggleButton.Bottom <= layout.Dock.Y && layout.Dock.Bottom <= height;
            transportSizes &= layout.ToggleButton.Width == 56 && layout.ToggleButton.Height == 56
                && layout.PreviousButton.Width == 44 && layout.PreviousButton.Height == 44
                && layout.NextButton.Width == 44 && layout.NextButton.Height == 44
                && layout.PreviousButton.X == 96 && layout.ToggleButton.X == 152 && layout.NextButton.X == 220;
        }
        check(transportPinned && transportInside && transportSizes,
            "transport row hangs exactly 16 DIP above the dock and stays fully on-card at 360-512 DIP");

        bool stacked = true;
        foreach (float height in new[] { 360f, 400f, 440f, 476f })
        {
            var layout = Resolve(360, height, true);
            stacked &= Near(layout.Timeline.Y, layout.ToggleButton.Y - 40)
                && layout.Timeline.X == 56 && layout.Timeline.Width == 248 && layout.Timeline.Height == 38
                && layout.Timeline.Bottom <= layout.ToggleButton.Y
                && !layout.Timeline.Intersects(layout.ToggleButton)
                && !layout.Timeline.Intersects(layout.Dock)
                && !layout.ToggleButton.Intersects(layout.Dock);
        }
        check(stacked, "timeline, transport row and dock stay stacked without overlap at every short height");

        bool covers = true;
        foreach (float height in new[] { 300f, 360f, 400f, 440f, 476f, 512f })
        {
            var layout = Resolve(360, height, true);
            covers &= layout.Cover.Y == 70 && Near(layout.Cover.Width, layout.Cover.Height)
                && Near(layout.Cover.X, (360 - layout.Cover.Width) / 2)
                && layout.Cover.Width >= 96 && layout.Cover.Width <= 168 && layout.CoverRadius == 16;
        }
        covers &= Resolve(360, 300, true).Cover.Width == 96
            && Resolve(360, 512, true).Cover.Width == 168
            && Near(Resolve(360, 360, true).Cover.Width, 98.4f)
            && Near(Resolve(360, 400, true).Cover.Width, 122.4f)
            && Near(Resolve(360, 440, true).Cover.Width, 146.4f);
        check(covers, "cover shrinks first from 168 to the 96 DIP floor, stays square and centred at y=70");

        var tight = Resolve(360, 400, true);
        var roomy = Resolve(360, 440, true);
        check(Near(tight.Title.Y, tight.Cover.Bottom + 6) && Near(tight.Artist.Y - tight.Title.Y, 24)
            && Near(roomy.Title.Y, roomy.Cover.Bottom + 8) && Near(roomy.Artist.Y - roomy.Title.Y, 28)
            && Near(baseline.Title.Y, baseline.Cover.Bottom + 8) && Near(baseline.Artist.Y - baseline.Title.Y, 28),
            "vertical gaps tighten 8->6 and the title/artist gap tightens 28->24 below 420 DIP");

        check(baseline.BodyViewport == new FloatRect(0, 250, 360, 46)
            && baseline.BodyContentHeight == 72 && baseline.MaxScroll == 26,
            "476 DIP body starts below the cover and its formula content needs 26 DIP of scroll with a timeline");

        var noTimeline = Resolve(360, 476, false);
        check(!noTimeline.HasTimeline && noTimeline.Timeline == baseline.Timeline
            && noTimeline.BodyViewport == new FloatRect(0, 250, 360, 82)
            && Near(noTimeline.BodyViewport.Bottom, noTimeline.ToggleButton.Y - 8)
            && noTimeline.BodyContentHeight == 72 && noTimeline.MaxScroll == 0,
            "without a timeline the body extends 8 DIP above the transport row and has nothing to scroll");

        bool bodyRules = true, emptyScroll = true, pluginScroll = true;
        foreach (float height in new[] { 360f, 400f, 440f, 476f })
            foreach (bool timeline in new[] { true, false })
                foreach (float plugin in new[] { 0f, 112f })
                {
                    var layout = Resolve(360, height, timeline, plugin);
                    float gap = height < 420 ? 6 : 8;
                    float titleGap = height < 420 ? 24 : 28;
                    float expectedTop = layout.Cover.Bottom + gap + 4;
                    float expectedBottom = timeline ? layout.Timeline.Y - 4 : layout.ToggleButton.Y - 8;
                    float expectedHeight = Math.Max(1, expectedBottom - expectedTop);
                    float expectedContent = 24 + titleGap + 20 + plugin;
                    bodyRules &= Near(layout.Title.Y, layout.Cover.Bottom + gap)
                        && Near(layout.Artist.Y, layout.Title.Y + titleGap)
                        && Near(layout.BodyViewport.Y, expectedTop)
                        && Near(layout.BodyViewport.Bottom, expectedBottom)
                        && Near(layout.BodyViewport.Height, expectedHeight)
                        && Near(layout.ArtworkViewport.Y, 56) && layout.ArtworkViewport.Width == 360
                        && Near(layout.ArtworkViewport.Bottom, layout.Title.Y)
                        && Near(layout.BodyContentHeight, expectedContent)
                        && Near(layout.MaxScroll, Math.Max(0, expectedContent - expectedHeight))
                        && float.IsFinite(layout.MaxScroll) && layout.MaxScroll >= 0
                        && layout.BodyViewport.Height >= 1 && layout.HasTimeline == timeline;
                    if (height >= 476 && !timeline && plugin == 0) emptyScroll &= layout.MaxScroll == 0;
                    if (plugin > 0) pluginScroll &= layout.MaxScroll > 0;
                }
        check(bodyRules, "body/artwork/content/scroll formulas hold for the height x timeline x plugin matrix");
        check(emptyScroll, "at or above 476 DIP a plugin-free page without a timeline reports no empty scroll");
        check(pluginScroll, "112 DIP of plugin content always produces real body scroll");

        check(Resolve(360, 512, false).MaxScroll == 0 && Resolve(360, 512, true).MaxScroll == 0,
            "476+ DIP plugin-free pages keep an empty scroll range");

        check(Throws(() => DetailsResponsiveLayout.Resolve(new DetailsResponsiveInput(new Vector2(float.NaN, 476), true)))
            && Throws(() => Resolve(360, float.PositiveInfinity, true))
            && Throws(() => Resolve(360, 476, true, float.NaN))
            && Throws(() => Resolve(float.NegativeInfinity, 476, true))
            && Throws(() => Resolve(360, 476, true, float.NegativeInfinity))
            && !Throws(() => Resolve(360, 476, true)),
            "non-finite size or plugin height throws ArgumentException while finite input resolves");

        var clamped = DetailsResponsiveLayout.Resolve(new DetailsResponsiveInput(new Vector2(360, 180), true));
        var minCard = Resolve(360, 300, true);
        check(clamped == minCard && Resolve(360, 476, true, -40) == baseline
            && Resolve(360, 512, true).Cover.Width == 168,
            "too-small finite heights clamp to 300, negative plugin height clamps to 0, taller cards keep the 168 cover");

        check(Resolve(300, 476, true).Home == new FloatRect(131, 14, 58, 28)
            && Resolve(300, 476, true).Dock == new FloatRect(0, 412, 320, 48)
            && baseline.Home == new FloatRect(151, 14, 58, 28),
            "widths below 320 DIP clamp the chrome to the 320 DIP minimum and stay centred");

        bool chromeInside = true;
        foreach (float height in new[] { 360f, 400f, 440f, 476f })
        {
            var layout = Resolve(360, height, true);
            chromeInside &= layout.Home == DetailsChromeLayout.Home(new Vector2(360, height))
                && layout.Dock == DetailsChromeLayout.Dock(new Vector2(360, height))
                && layout.Home.X >= 0 && layout.Home.Bottom <= height
                && layout.Dock.X >= 0 && layout.Dock.Right <= 360 && Near(layout.Dock.Bottom, height - 16);
        }
        check(chromeInside, "home handle and dock come from DetailsChromeLayout and stay on-card at every height");

        bool hits = true;
        foreach (float height in new[] { 300f, 360f, 400f, 440f, 476f })
        {
            var size = new Vector2(360, height);
            var homeHit = DetailsChromeLayout.HomeHit(size);
            var dockItem = DetailsChromeLayout.DockItem(size, 0);
            hits &= homeHit.Width >= 44 && homeHit.Height >= 44
                && homeHit.Y >= 0 && homeHit.Bottom <= height
                && dockItem.Width >= 44 && dockItem.Height >= 44;
        }
        var shortCard = Resolve(360, 360, true);
        hits &= shortCard.PreviousButton.Width >= 44 && shortCard.PreviousButton.Height >= 44
            && shortCard.ToggleButton.Width >= 44 && shortCard.ToggleButton.Height >= 44
            && shortCard.NextButton.Width >= 44 && shortCard.NextButton.Height >= 44;
        check(hits, "every button, the home handle and dock destinations keep hit targets of at least 44x44 DIP");

        bool shortVisible = true;
        foreach (float height in new[] { 360f, 400f, 440f })
        {
            var layout = Resolve(360, height, true);
            shortVisible &= layout.ToggleButton.Bottom <= layout.Dock.Y
                && layout.PreviousButton.Bottom <= layout.Dock.Y
                && layout.NextButton.Bottom <= layout.Dock.Y
                && layout.ToggleButton.Bottom <= height && layout.NextButton.Right <= 360
                && layout.Home.Bottom <= height;
        }
        check(shortVisible, "on 360/400/440 DIP the whole transport row stays visible above the dock");
    }
}
