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
    bool ExtremeCompact,
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

    /// <summary>
    /// Below this height the vertical recipe cannot keep a readable body between the fixed
    /// timeline/transport/dock bands, so the page switches to a horizontal extreme layout:
    /// a small cover on the left, title/artist on the right, and the body clipped below both.
    /// </summary>
    public const float ExtremeCompactHeight = 360;
    public const float ExtremeCoverSize = 48;
    public const float ExtremeCoverX = 24;
    public const float ExtremeTextX = 84;

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
        // legitimately report less than one card after a scaling or monitor change. Width is
        // clamped into the card range too so render, hit and UIA work in one local space.
        float height = Math.Max(MinHeight, input.Size.Y);
        float width = Math.Clamp(input.Size.X, MinWidth, CardWidth);
        var chromeSize = new Vector2(width, height);

        bool compact = height < PreferredHeight;
        bool extreme = height < ExtremeCompactHeight;
        bool tight = height < TightSpacingHeight;
        float gap = tight ? TightGap : Gap;
        float titleGap = tight ? TightTitleGap : TitleGap;
        // At 320 DIP the 360 DIP content columns are shifted left as one block. The chrome
        // already centres itself on the real width, so every content rect uses this offset.
        float offsetX = (width - CardWidth) / 2;

        FloatRect home = DetailsChromeLayout.Home(chromeSize);
        FloatRect dock = DetailsChromeLayout.Dock(chromeSize);

        // Transport/timeline stay pinned to the dock on every height.
        float transportBottom = dock.Y - TransportDockInset;
        float toggleY = transportBottom - ToggleSize;
        float sideY = toggleY + SideButtonLift;
        var previous = new FloatRect(SideButtonX + offsetX, sideY, SideButtonSize, SideButtonSize);
        var toggle = new FloatRect(ToggleX + offsetX, toggleY, ToggleSize, ToggleSize);
        var next = new FloatRect(SideButtonNextX + offsetX, sideY, SideButtonSize, SideButtonSize);
        var timeline = new FloatRect(TimelineX + offsetX, toggleY - TimelineLift, TimelineWidth, TimelineHeight);

        if (extreme)
        {
            // Extreme compact keeps a readable body: a 48 DIP cover on the left, title/artist
            // to its right, and the body clip starting under the home handle. Nothing touches
            // the fixed timeline/transport/dock bands and the body keeps the space between.
            var extremeCover = new FloatRect(ExtremeCoverX + offsetX, ArtworkTop, ExtremeCoverSize, ExtremeCoverSize);
            float textX = ExtremeTextX + offsetX;
            float textWidth = Math.Max(1, width - textX - 24);
            var extremeTitle = new FloatRect(textX, ArtworkTop, textWidth, TitleHeight);
            var extremeArtist = new FloatRect(textX, extremeTitle.Y + TightGap + TitleHeight, textWidth, ArtistHeight);
            float extremeBodyTop = ArtworkTop;
            float extremeBodyBottom = input.HasTimeline ? timeline.Y - TimelineBodyInset : toggleY - BodyNoTimelineInset;
            var extremeBody = new FloatRect(0, extremeBodyTop, width, Math.Max(1, extremeBodyBottom - extremeBodyTop));
            float extremeContentBottom = Math.Max(extremeTitle.Bottom, extremeArtist.Bottom);
            float extremeContent = extremeContentBottom - extremeBodyTop + Math.Max(0, input.PluginContentHeight);
            float extremeScroll = Math.Max(0, extremeContent - extremeBody.Height);
            var extremeArtwork = new FloatRect(0, ArtworkTop, width,
                Math.Max(1, extremeCover.Bottom + gap - ArtworkTop));
            return new(compact, true, home, dock, extremeCover, CoverRadius, extremeTitle, extremeArtist,
                timeline, previous, toggle, next, extremeArtwork, extremeBody, extremeContent,
                extremeScroll, input.HasTimeline);
        }

        // The cover shrinks first. Above 400 DIP it stays in the 96..168 range; below 400 it
        // may continue toward 72 so the body keeps a usable viewport at 360.
        float coverSize = height >= CoverTightHeight
            ? Math.Clamp(CoverPreferredSize - CoverShrinkPerDip * (PreferredHeight - height),
                CoverMinimumSize, CoverPreferredSize)
            : Math.Clamp(CoverMinimumSize - CoverTightShrinkPerDip * (CoverTightHeight - height),
                CoverTightMinimumSize, CoverMinimumSize);
        var cover = new FloatRect((width - coverSize) / 2, CoverTop, coverSize, coverSize);
        float coverBottom = cover.Bottom;
        var title = new FloatRect(TitleX + offsetX, coverBottom + gap, TitleWidth, TitleHeight);
        var artist = new FloatRect(TitleX + offsetX, title.Y + titleGap, TitleWidth, ArtistHeight);

        // Only the body scrolls. Its clip starts exactly at the title top (nothing of the title
        // is shaved off) and ends 4 DIP above the timeline, or 8 DIP above the transport row
        // when the session exposes no timeline.
        float bodyTop = title.Y;
        float bodyBottom = input.HasTimeline ? timeline.Y - TimelineBodyInset : toggleY - BodyNoTimelineInset;
        var bodyViewport = new FloatRect(0, bodyTop, width, Math.Max(1, bodyBottom - bodyTop));
        float contentBottom = Math.Max(title.Bottom, artist.Bottom);
        float bodyContentHeight = contentBottom - bodyTop + Math.Max(0, input.PluginContentHeight);
        float maxScroll = Math.Max(0, bodyContentHeight - bodyViewport.Height);

        // Artwork clip: from below the home handle to the title top, so the shrinking cover
        // can never bleed into the title or the fixed bottom bands.
        var artworkViewport = new FloatRect(0, ArtworkTop, width, Math.Max(1, coverBottom + gap - ArtworkTop));

        return new(compact, false, home, dock, cover, CoverRadius, title, artist, timeline,
            previous, toggle, next, artworkViewport, bodyViewport, bodyContentHeight, maxScroll,
            input.HasTimeline);
    }
}
