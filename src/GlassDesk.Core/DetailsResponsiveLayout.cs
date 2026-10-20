using System.Numerics;

namespace GlassDesk.Core;

/// <summary>
/// Everything the Details/music page needs from the runtime before its geometry can be
/// resolved: the available card size, whether the media session exposes a real timeline,
/// and the total height of plugin content placed below the title/artist block.
/// </summary>
public readonly record struct DetailsResponsiveInput(Vector2 Size, bool HasTimeline, float PluginContentHeight = 0);

/// <summary>
/// Fully resolved Details/music geometry in DIP (origin top-left). <see cref="Compact"/>
/// is true below <see cref="DetailsResponsiveLayout.PreferredHeight"/>: the cover and the
/// vertical gaps shrink first while the home handle, timeline, transport row and dock stay
/// pinned to the card. Every transport target is at least 44×44 and only
/// <see cref="BodyViewport"/> may scroll.
/// </summary>
public sealed record DetailsResponsiveResult(
    bool Compact,
    FloatRect Home,
    FloatRect Dock,
    FloatRect Cover,
    float CoverRadius,
    FloatRect Title,
    FloatRect Artist,
    FloatRect Timeline,
    FloatRect PreviousButton,
    FloatRect ToggleButton,
    FloatRect NextButton,
    FloatRect ArtworkViewport,
    FloatRect BodyViewport,
    float BodyContentHeight,
    float MaxScroll,
    bool HasTimeline);

/// <summary>
/// Pure, App-independent geometry for the responsive Details/music page. Every band is
/// derived from the available height so a short workspace can never clip the transport row
/// or push it into the dock: the home handle and dock are the frozen
/// <see cref="DetailsChromeLayout"/> geometry, the transport row hangs exactly 16 DIP above
/// the dock, the timeline sits 40 DIP above the transport row, and the middle body is the
/// only vertical region allowed to scroll (no scroll at all when nothing overflows).
/// </summary>
public static class DetailsResponsiveLayout
{
    /// <summary>Full music page height; at or above it every frozen 476 DIP value is kept.</summary>
    public const float PreferredHeight = 476;

    /// <summary>Smallest card height the geometry supports; smaller finite inputs clamp here.</summary>
    public const float MinHeight = 300;

    /// <summary>Smallest card width the chrome supports; smaller finite inputs clamp here.</summary>
    public const float MinWidth = 320;

    /// <summary>Frozen Details card width. Content columns are centred on it, not on the monitor.</summary>
    public const float CardWidth = DetailsChromeLayout.CardWidth;

    public const float CoverTop = 70;
    public const float CoverPreferredSize = 168;
    /// <summary>Cover floor at or above <see cref="CoverTightHeight"/> (the normal compact range).</summary>
    public const float CoverMinimumSize = 96;
    public const float CoverShrinkPerDip = .60f;
    /// <summary>Below this height the cover may keep shrinking toward <see cref="CoverTightMinimumSize"/>.</summary>
    public const float CoverTightHeight = 400;
    public const float CoverTightMinimumSize = 72;
    public const float CoverTightShrinkPerDip = .8f;
    public const float CoverRadius = 16;
    public const float TitleX = 24, TitleWidth = 312, TitleHeight = 24, ArtistHeight = 20;
    /// <summary>Below this height the cover/title gaps tighten (the second compact stage).</summary>
    public const float TightSpacingHeight = 420;
    public const float Gap = 8, TightGap = 6;
    public const float TitleGap = 28, TightTitleGap = 24;
    public const float TimelineX = 56, TimelineWidth = 248, TimelineHeight = 38;
    public const float TimelineLift = 40, TimelineBodyInset = 4;
    public const float ToggleX = 152, ToggleSize = 56;
    public const float SideButtonX = 96, SideButtonNextX = 220, SideButtonSize = 44, SideButtonLift = 6;
    public const float TransportDockInset = 16;
    public const float BodyNoTimelineInset = 8;
    public const float ArtworkTop = 56;

    /// <summary>
    /// Resolves the complete Details/music geometry for one available size. Finite inputs are
    /// clamped into the supported range (height to <see cref="MinHeight"/>, width to
    /// <see cref="MinWidth"/>, negative plugin height to 0); non-finite input is a programming
    /// error and throws <see cref="ArgumentException"/> instead of poisoning the rects.
    /// </summary>
    public static DetailsResponsiveResult Resolve(DetailsResponsiveInput input)
    {
        if (!float.IsFinite(input.Size.X) || !float.IsFinite(input.Size.Y))
            throw new ArgumentException("Details size must be finite.", nameof(input));
        if (!float.IsFinite(input.PluginContentHeight))
            throw new ArgumentException("Plugin content height must be finite.", nameof(input));

        // Finite but too small surfaces are clamped instead of rejected: a work area may
        // legitimately report less than one card after a scaling or monitor change.
        float height = Math.Max(MinHeight, input.Size.Y);
        var chromeSize = new Vector2(Math.Max(MinWidth, input.Size.X), height);

        bool compact = height < PreferredHeight;
        bool tight = height < TightSpacingHeight;
        float gap = tight ? TightGap : Gap;
        float titleGap = tight ? TightTitleGap : TitleGap;

        FloatRect home = DetailsChromeLayout.Home(chromeSize);
        FloatRect dock = DetailsChromeLayout.Dock(chromeSize);

        // Transport row: always 16 DIP above the dock, then the timeline above it. Deriving
        // both from the dock is what keeps the row reachable on a 360 DIP-tall workspace.
        float transportBottom = dock.Y - TransportDockInset;
        float toggleY = transportBottom - ToggleSize;
        float sideY = toggleY + SideButtonLift;
        var previous = new FloatRect(SideButtonX, sideY, SideButtonSize, SideButtonSize);
        var toggle = new FloatRect(ToggleX, toggleY, ToggleSize, ToggleSize);
        var next = new FloatRect(SideButtonNextX, sideY, SideButtonSize, SideButtonSize);
        var timeline = new FloatRect(TimelineX, toggleY - TimelineLift, TimelineWidth, TimelineHeight);

        // The cover shrinks first. Above 400 DIP it stays in the 96..168 range; below 400 it
        // may continue toward 72 so the body keeps a usable viewport at 360. It stays square
        // and centred on the 360 DIP card, and at 476+ the frozen 168 DIP cover is kept.
        float coverSize = height >= CoverTightHeight
            ? Math.Clamp(CoverPreferredSize - CoverShrinkPerDip * (PreferredHeight - height),
                CoverMinimumSize, CoverPreferredSize)
            : Math.Clamp(CoverMinimumSize - CoverTightShrinkPerDip * (CoverTightHeight - height),
                CoverTightMinimumSize, CoverMinimumSize);
        var cover = new FloatRect((CardWidth - coverSize) / 2, CoverTop, coverSize, coverSize);
        float coverBottom = cover.Bottom;
        var title = new FloatRect(TitleX, coverBottom + gap, TitleWidth, TitleHeight);
        var artist = new FloatRect(TitleX, title.Y + titleGap, TitleWidth, ArtistHeight);

        // Only the body scrolls. Its clip starts exactly at the title top (nothing of the title
        // is shaved off) and ends 4 DIP above the timeline, or 8 DIP above the transport row
        // when the session exposes no timeline.
        float bodyTop = title.Y;
        float bodyBottom = input.HasTimeline ? timeline.Y - TimelineBodyInset : toggleY - BodyNoTimelineInset;
        var bodyViewport = new FloatRect(0, bodyTop, CardWidth, Math.Max(1, bodyBottom - bodyTop));
        // The scrollable content runs from the title top to the lower of the title/artist
        // rects, then continues with the plugin contributions.
        float contentBottom = Math.Max(title.Bottom, artist.Bottom);
        float bodyContentHeight = contentBottom - title.Y + Math.Max(0, input.PluginContentHeight);
        float maxScroll = Math.Max(0, bodyContentHeight - bodyViewport.Height);

        // Artwork clip: from below the home handle to the title top, so the shrinking cover
        // can never bleed into the title or the fixed bottom bands.
        var artworkViewport = new FloatRect(0, ArtworkTop, CardWidth, Math.Max(1, coverBottom + gap - ArtworkTop));

        return new(compact, home, dock, cover, CoverRadius, title, artist, timeline,
            previous, toggle, next, artworkViewport, bodyViewport, bodyContentHeight, maxScroll,
            input.HasTimeline);
    }
}
