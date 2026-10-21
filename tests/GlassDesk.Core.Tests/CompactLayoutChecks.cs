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
                && Near(layout.Timeline.Bottom, layout.ToggleButton.Y - 2)
                && !layout.Timeline.Intersects(layout.ToggleButton)
                && !layout.Timeline.Intersects(layout.Dock)
                && !layout.ToggleButton.Intersects(layout.Dock);
        }
        check(stacked, "timeline bottom clears the transport top by the frozen 2 DIP and never overlaps the dock");

        bool covers = true;
        foreach (float height in new[] { 360f, 380f, 399f, 400f, 440f, 476f, 512f })
        {
            var layout = Resolve(360, height, true);
            float floor = height >= 400 ? 96 : 72;
            float ceiling = height >= 400 ? 168 : 96;
            covers &= layout.Cover.Y == 70 && Near(layout.Cover.Width, layout.Cover.Height)
                && Near(layout.Cover.X, (360 - layout.Cover.Width) / 2)
                && layout.Cover.Width >= floor && layout.Cover.Width <= ceiling && layout.CoverRadius == 16;
        }
        covers &= Resolve(360, 360, true).Cover.Width == 72 && Near(Resolve(360, 380, true).Cover.Width, 80)
            && Near(Resolve(360, 399, true).Cover.Width, 95.2f) && Near(Resolve(360, 400, true).Cover.Width, 122.4f)
            && Near(Resolve(360, 440, true).Cover.Width, 146.4f)
            && Resolve(360, 512, true).Cover.Width == 168;
        check(covers, "cover shrinks 168->96 in normal compact and may reach 72 from 360 to below 400 DIP, always square and centred");

        var extreme300 = Resolve(360, 300, true);
        var extreme320 = Resolve(360, 320, true);
        var extreme340 = Resolve(360, 340, true);
        check(extreme300.ExtremeCompact && extreme320.ExtremeCompact && extreme340.ExtremeCompact
            && !Resolve(360, 360, true).ExtremeCompact
            && extreme300.Cover == new FloatRect(24, 56, 48, 48)
            && extreme300.Title == new FloatRect(84, 56, 252, 24)
            && extreme300.Artist == new FloatRect(84, 86, 252, 20)
            && extreme300.BodyViewport == new FloatRect(0, 56, 360, 64)
            && extreme300.BodyContentHeight == 50 && extreme300.MaxScroll == 0
            && extreme320.BodyViewport.Height == 84 && extreme340.BodyViewport.Height == 104
            && !extreme300.Cover.Intersects(extreme300.Timeline)
            && !extreme300.Title.Intersects(extreme300.Timeline)
            && !extreme300.Artist.Intersects(extreme300.Timeline)
            && Near(extreme300.Timeline.Bottom, extreme300.ToggleButton.Y - 2)
            && extreme300.BodyViewport.Bottom <= extreme300.Timeline.Y
            && extreme300.ToggleButton.Bottom <= extreme300.Dock.Y,
            "below 360 DIP the page switches to a horizontal cover+title body with a readable viewport and no timeline overlap");

        check(Resolve(360, 300, true, 112).MaxScroll > 0
            && Near(Resolve(360, 300, true, 112).MaxScroll, 98),
            "extreme compact still scrolls plugin content instead of clipping it");

        var tight = Resolve(360, 400, true);
        var roomy = Resolve(360, 440, true);
        check(Near(tight.Title.Y, tight.Cover.Bottom + 6) && Near(tight.Artist.Y - tight.Title.Y, 24)
            && Near(roomy.Title.Y, roomy.Cover.Bottom + 8) && Near(roomy.Artist.Y - roomy.Title.Y, 28)
            && Near(baseline.Title.Y, baseline.Cover.Bottom + 8) && Near(baseline.Artist.Y - baseline.Title.Y, 28),
            "vertical gaps tighten 8->6 and the title/artist gap tightens 28->24 below 420 DIP");

        check(baseline.BodyViewport == new FloatRect(0, 246, 360, 50)
            && Near(baseline.BodyViewport.Y, baseline.Title.Y)
            && baseline.BodyContentHeight == 48 && baseline.MaxScroll == 0,
            "476 DIP body clips from the title top: 48 DIP of title/artist content fits its 50 DIP viewport");

        var noTimeline = Resolve(360, 476, false);
        check(!noTimeline.HasTimeline && noTimeline.Timeline == baseline.Timeline
            && noTimeline.BodyViewport == new FloatRect(0, 246, 360, 86)
            && Near(noTimeline.BodyViewport.Y, noTimeline.Title.Y)
            && noTimeline.BodyContentHeight == 48 && noTimeline.MaxScroll == 0,
            "without a timeline the body runs to 8 DIP above the transport row and still needs no scroll");

        check(Resolve(360, 476, true).MaxScroll == 0 && Resolve(360, 476, false).MaxScroll == 0
            && Resolve(360, 480, true).MaxScroll == 0 && Resolve(360, 512, true).MaxScroll == 0
            && Resolve(360, 512, false).MaxScroll == 0,
            "476+ DIP plugin-free pages report zero scroll with or without a timeline");

        var withPlugin = Resolve(360, 476, true, 112);
        check(withPlugin.BodyContentHeight == 160 && withPlugin.BodyViewport.Height == 50
            && withPlugin.MaxScroll == 110,
            "476 DIP with a 112 DIP plugin contribution scrolls 110 DIP (160 content - 50 viewport)");

        check(Resolve(360, 400, true).MaxScroll > 0 && Near(Resolve(360, 400, true).MaxScroll, 22.4f)
            && Resolve(360, 440, true).MaxScroll > 0 && Near(Resolve(360, 440, true).MaxScroll, 12.4f),
            "400/440 DIP with a timeline still need real body scroll for a plugin-free page");

        var short360 = Resolve(360, 360, true);
        check(short360.BodyViewport.Height >= 24 && Near(short360.BodyViewport.Y, short360.Title.Y)
            && Near(short360.Timeline.Bottom, short360.ToggleButton.Y - 2)
            && !short360.Timeline.Intersects(short360.ToggleButton)
            && !short360.ToggleButton.Intersects(short360.Dock)
            && short360.ToggleButton.Bottom <= short360.Dock.Y,
            "360 DIP keeps at least a 24 DIP body viewport while timeline/transport/dock stay separated");

        bool bodyRules = true, emptyScroll = true, pluginScroll = true;
        foreach (float height in new[] { 360f, 400f, 440f, 476f })
            foreach (bool timeline in new[] { true, false })
                foreach (float plugin in new[] { 0f, 112f })
                {
                    var layout = Resolve(360, height, timeline, plugin);
                    float gap = height < 420 ? 6 : 8;
                    float titleGap = height < 420 ? 24 : 28;
                    float expectedTop = layout.Title.Y;
                    float expectedBottom = timeline ? layout.Timeline.Y - 4 : layout.ToggleButton.Y - 8;
                    float expectedHeight = Math.Max(1, expectedBottom - expectedTop);
                    float expectedContent = Math.Max(layout.Title.Bottom, layout.Artist.Bottom) - layout.Title.Y + plugin;
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
                    if (plugin == 0 && (!timeline || height >= 476)) emptyScroll &= layout.MaxScroll == 0;
                    if (plugin > 0) pluginScroll &= layout.MaxScroll > 0;
                }
        check(bodyRules, "body/artwork/content/scroll formulas hold for the height x timeline x plugin matrix");
        check(emptyScroll, "plugin-free pages avoid empty scroll unless a timeline squeezes the short viewport");
        check(pluginScroll, "112 DIP of plugin content always produces real body scroll");

        check(Resolve(360, 512, false).MaxScroll == 0 && Resolve(360, 512, true).MaxScroll == 0,
            "512 DIP plugin-free pages keep an empty scroll range");

        var width320 = Resolve(320, 476, true);
        check(width320.Home == new FloatRect(131, 14, 58, 28)
            && width320.Dock == new FloatRect(0, 412, 320, 48)
            && width320.Cover == new FloatRect(76, 70, 168, 168)
            && width320.Title == new FloatRect(4, 246, 312, 24)
            && width320.Timeline == new FloatRect(36, 300, 248, 38)
            && width320.PreviousButton == new FloatRect(76, 346, 44, 44)
            && width320.ToggleButton == new FloatRect(132, 340, 56, 56)
            && width320.NextButton == new FloatRect(200, 346, 44, 44)
            && width320.BodyViewport == new FloatRect(0, 246, 320, 50)
            && width320.ArtworkViewport == new FloatRect(0, 56, 320, 190)
            && width320.Title.Right <= 320 && width320.Timeline.Right <= 320
            && Near(width320.Timeline.X, width320.Title.X + 32),
            "320 DIP width shifts cover/title/timeline/transport as one block and keeps body/artwork at the real width");

        var extremeWidth320 = Resolve(320, 320, true);
        check(extremeWidth320.Cover == new FloatRect(4, 56, 48, 48)
            && extremeWidth320.Title == new FloatRect(64, 56, 232, 24)
            && extremeWidth320.BodyViewport == new FloatRect(0, 56, 320, 84)
            && extremeWidth320.Timeline == new FloatRect(36, 144, 248, 38)
            && extremeWidth320.Title.Right <= 320,
            "extreme compact also follows the real 320 DIP width for cover/title/body");

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
