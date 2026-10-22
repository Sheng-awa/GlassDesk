using System.Collections.Frozen;
using System.Text;

namespace GlassDesk.Core;

// Values are shared with native typography ABI V2. Append; never renumber.
public enum TextRole : uint
{
    Legacy=0, ClockCapsule=1, ClockWhiteplate=2, ClockLegacyAux=3, ClockPeekHero=4,
    MediaCapsuleTitle=5, MediaCapsuleArtist=6, MediaPeekTitle=7, MediaPeekArtist=8,
    MediaDetailsTitle=9, MediaDetailsArtist=10, PageTitle=11, SectionTitle=12,
    Body=13, Label=14, Caption=15, CaptionSettings=16, NavigationSettings=17,
    NavigationSemantic=18, WidgetClock=19, WidgetDateValue=20, WidgetValue=21, WidgetLabel=22
}
[Flags] public enum TextFlags : uint { None=0, TabularNumbers=1, TabularDigitsOnly=2,
    ClockSerif=0x100, ClockGeometric=0x200, ClockMono=0x300 }
public enum OpticalRole { Small, Text, Display }
public enum FontProfile { SystemA=0, Baseline=1, NotoQa=2 }
public enum InkRole { Primary=1, Secondary=2, Caption=3, Disabled=4 }

// Installed Windows faces only; the stable IDs are persisted, not arbitrary family names.
public sealed record ClockFontChoice(string Id, string Name, string Family, TextFlags Flag);
public static class ClockFontCatalog
{
    public const string System="system";
    public static IReadOnlyList<ClockFontChoice> All {get;}=Array.AsReadOnly(new[] {
        new ClockFontChoice(System,"默认", "Segoe UI Variable Display",TextFlags.None),
        new ClockFontChoice("serif","书卷", "Cambria",TextFlags.ClockSerif),
        new ClockFontChoice("geometric","几何", "Bahnschrift",TextFlags.ClockGeometric),
        new ClockFontChoice("mono","等宽", "Consolas",TextFlags.ClockMono)
    });
    public static string Normalize(string? id)=>All.FirstOrDefault(x=>x.Id==id)?.Id??System;
    public static ClockFontChoice Get(string? id)=>All.First(x=>x.Id==Normalize(id));
    public static bool IsClockRole(TextRole role)=>role is TextRole.ClockCapsule or TextRole.ClockWhiteplate or TextRole.ClockPeekHero;
}

public sealed record TypographyToken(TextRole Role,string Key,float FontSizeDip,float LineHeightDip,
    int LatinWeight,int CjkWeight,float TrackingEm,OpticalRole Optical,TextFlags Flags=TextFlags.None);

public static class TypographyCatalog
{
    public const int Revision=1;
    static TypographyToken T(TextRole r,string key,float size,float line,int latin=400,int cjk=400,
        float tracking=0,OpticalRole optical=OpticalRole.Text,TextFlags flags=TextFlags.None)
        =>new(r,key,size,line,latin,cjk,tracking,optical,flags);
    public static IReadOnlyDictionary<TextRole,TypographyToken> All {get;}=new[]
    {
        T(TextRole.ClockCapsule,"clock.capsule",25,32,optical:OpticalRole.Display,flags:TextFlags.TabularNumbers),
        T(TextRole.ClockWhiteplate,"clock.whiteplate",16,26,flags:TextFlags.TabularNumbers),
        T(TextRole.ClockLegacyAux,"clock.legacyAux",10,12,optical:OpticalRole.Small),
        T(TextRole.ClockPeekHero,"clock.peekHero",34,44,optical:OpticalRole.Display,flags:TextFlags.TabularNumbers),
        T(TextRole.MediaCapsuleTitle,"media.capsuleTitle",14,20,500),
        T(TextRole.MediaCapsuleArtist,"media.capsuleArtist",12,16,optical:OpticalRole.Small),
        T(TextRole.MediaPeekTitle,"media.peekTitle",18,26,600,500),
        T(TextRole.MediaPeekArtist,"media.peekArtist",13,18),
        T(TextRole.MediaDetailsTitle,"media.detailsTitle",20,28,600,500,-.01f,OpticalRole.Display),
        T(TextRole.MediaDetailsArtist,"media.detailsArtist",14,20),
        T(TextRole.PageTitle,"page.title",20,28,600,500,-.01f,OpticalRole.Display),
        T(TextRole.SectionTitle,"section.title",16,24,600,500),
        T(TextRole.Body,"body",14,20), T(TextRole.Label,"label",14,20,500),
        T(TextRole.Caption,"caption",12,16,optical:OpticalRole.Small),
        T(TextRole.CaptionSettings,"caption.settings",13,18),
        T(TextRole.NavigationSettings,"navigation.settings",13,18),
        T(TextRole.NavigationSemantic,"navigation.semantic",12,16,500,optical:OpticalRole.Small),
        T(TextRole.WidgetClock,"widget.clock",36,44,optical:OpticalRole.Display,flags:TextFlags.TabularNumbers),
        T(TextRole.WidgetDateValue,"widget.dateValue",48,56,optical:OpticalRole.Display,flags:TextFlags.TabularDigitsOnly),
        T(TextRole.WidgetValue,"widget.value",36,44,optical:OpticalRole.Display,flags:TextFlags.TabularNumbers),
        T(TextRole.WidgetLabel,"widget.label",12,16,500,optical:OpticalRole.Small)
    }.ToFrozenDictionary(t=>t.Role);

    public static TypographyToken Get(TextRole role)=>All.TryGetValue(role,out var token)
        ?token:throw new ArgumentOutOfRangeException(nameof(role));
    // The protected Details title slot is 24 DIP high while its role line box is
    // 28 DIP. P2 unclipped glyph measurements require this optical baseline;
    // geometry, font size and all other role slots keep their existing values.
    public static float BaselineForSlot(TextRole role,float heightDip)
        =>role==TextRole.MediaDetailsTitle&&heightDip==24?18.75f:0;
    public static uint Ink(InkRole role,bool darkMaterial=false)=> (darkMaterial,role) switch
    {
        (false,InkRole.Primary)=>0xff20252c,(false,InkRole.Secondary)=>0xff48515b,
        (false,InkRole.Caption)=>0xff626c78,(false,InkRole.Disabled)=>0xff8e98a3,
        (true,InkRole.Primary)=>0xfff8fafc,(true,InkRole.Secondary)=>0xffdfe5ec,
        (true,InkRole.Caption)=>0xffb4c0cc,(true,InkRole.Disabled)=>0xff84909c,
        _=>throw new ArgumentOutOfRangeException(nameof(role))
    };
}

public static class FontResolutionPolicy
{
    public static IReadOnlyList<string> Families(OpticalRole optical)=>optical switch
    {
        OpticalRole.Small=>["Segoe UI Variable Small","Segoe UI Variable Text","Segoe UI"],
        OpticalRole.Display=>["Segoe UI Variable Display","Segoe UI Variable Text","Segoe UI"],
        _=>["Segoe UI Variable Text","Segoe UI Variable","Segoe UI"]
    };
    // Negative tracking is restricted to wholly Latin headings; fallback runs stay natural.
    public static bool IsPureLatinTitle(string text)=>text.Length>0 && text.EnumerateRunes().All(r=>
        r.Value<=0x024f || r.Value is >=0x2000 and <=0x206f);
    public static float Tracking(TypographyToken token,string text)=>token.Flags!=TextFlags.None
        || !IsPureLatinTitle(text)?0:token.TrackingEm;
}
