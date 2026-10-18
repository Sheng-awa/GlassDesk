using System.Numerics;
namespace GlassDesk.Core;

public static class CompanionAppearanceIds
{
    public const string Original="glassdesk.orb.original", CatEar="glassdesk.orb.cat-ear", Devil="glassdesk.orb.devil";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Original, CatEar, Devil });
    public static string Normalize(string? value)=>value is CatEar or Devil?value:Original;
}

public enum PointerCaptureOwner { None, WidgetDrag, Timeline, CompanionReveal, Navigation, SettingsNavigation, WidgetBoard }
public enum CompanionPlacementMode { Front, NavigationIntro, CoverPeek, Revealing }
public enum CompanionShapePose { Rounded, Stretch, Squash, Droplet, Peek }
public enum NavigationOuterState { Collapsed, HoverPending, Opening, Open, ClosePending, Closing }
public readonly record struct NavigationState(long OpenSequence,NavigationOuterState OuterState,string? HoveredItemId,
    string SelectedPreviewItemId,bool IntroConsumed,PointerCaptureOwner CaptureOwner=PointerCaptureOwner.None);
public sealed record NavigationEntry(string Id,string Label,int ActionId);
public static class PreviewNavigationItems
{
    public static IReadOnlyList<NavigationEntry> All {get;}=Array.AsReadOnly(new NavigationEntry[]
    {
        new NavigationEntry("music","音乐",100),new("clock","时钟",101),new("companion","陪伴",102),
        new("focus","专注",103),new("notes","随记",104)
    });
}
public readonly record struct CompanionPose(Vector2 BodyAnchor,FloatRect FullVisualBounds,CompanionPlacementMode PlacementMode,
    CompanionShapePose ShapePose,string? OccluderId=null);
public sealed record HideAnchor(string StableId,FloatRect OccluderBounds,float OccluderRadius,Vector2 BodyAnchor,
    FloatRect VisibleEyeRegion,FloatRect RevealHitRegion);

public readonly record struct FloatRect(float X,float Y,float Width,float Height)
{
    public float Right=>X+Width;public float Bottom=>Y+Height;
    public bool Contains(float x,float y)=>x>=X&&y>=Y&&x<Right&&y<Bottom;
    public bool Intersects(FloatRect other)=>X<other.Right&&Right>other.X&&Y<other.Bottom&&Bottom>other.Y;
    public FloatRect Inflate(float amount)=>new(X-amount,Y-amount,Width+2*amount,Height+2*amount);
}
