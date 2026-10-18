using GlassDesk.Core;

namespace GlassDesk.NativeBridge;

public enum SurfaceOpKind { Rectangle, RoundedRectangle, Text, Image, Line, Ellipse, Icon, MultilineText }

/// <summary>Generic paint command. Coordinates, font size and radius are DIP; Color is ARGB.</summary>
public sealed record SurfaceOp
{
    public SurfaceOpKind Kind { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public float Radius { get; init; }
    public float FontSize { get; init; }=14;
    public uint Color { get; init; }=0xff202632;
    public string? Text { get; init; }
    public byte[]? Image { get; init; }
    public bool Bold { get; init; }
    public bool Center { get; init; }
    public int FontWeight { get; init; }
    public float TrackingEm { get; init; }
    public TextRole TextRole { get; init; }
    public float LineHeightDip { get; init; }
    public float BaselineDip { get; init; }
    public TextFlags TextFlags { get; init; }
    public string? LocaleName { get; init; }

    public static SurfaceOp Typography(string text, TextRole role, float x, float y, float width, float height,
        uint color, bool center=false, TextFlags? numericFlags=null, string? locale=null)
    {
        if(!TypographyCatalog.All.TryGetValue(role,out var token))throw new ArgumentOutOfRangeException(nameof(role));
        var flags=numericFlags??token.Flags;
        return new() {Kind=SurfaceOpKind.Text,Text=text,TextRole=role,X=x,Y=y,Width=width,Height=height,Color=color,
            Center=center,FontSize=token.FontSizeDip,FontWeight=token.LatinWeight,
            TrackingEm=flags==TextFlags.None?FontResolutionPolicy.Tracking(token,text):0,LineHeightDip=Math.Min(token.LineHeightDip,height),
            BaselineDip=TypographyCatalog.BaselineForSlot(role,height),TextFlags=flags,LocaleName=locale};
    }
}
