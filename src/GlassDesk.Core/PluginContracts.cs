using System.Text.Json;

namespace GlassDesk.Core;

public static class PluginApi { public const int Version=1; }
public enum PluginRunState { Disabled, Starting, Running, Paused, Stopped, Faulted, RestartRequired }
public enum CompanionExpression { Calm, Curious, Happy, Sleepy, Surprised, Blink }
public sealed record CompanionVisualState(CompanionExpression Expression,uint Color,float EyeOffsetX=0,float EyeOffsetY=0,float ScaleX=1,float ScaleY=1,float Tilt=0,int TransitionMs=220)
{
    public string AppearanceId {get;init;}=CompanionAppearanceIds.Original;
    public CompanionVisualState(CompanionExpression expression,uint color,float eyeOffsetX,float eyeOffsetY,float scaleX,float scaleY)
        :this(expression,color,eyeOffsetX,eyeOffsetY,scaleX,scaleY,0,220) { }
}
public enum CompanionInteraction { None, Attention, Press, Drag, Release }
public interface ICompanionAppearance
{
    string Id {get;}
    CompanionVisualState Present(CompanionExpression expression,uint color);
}
public sealed record PluginSceneState(WidgetForm Form,bool Visible,MediaSnapshot Media,bool ReducedMotion,long Timestamp,CompanionInteraction Interaction=CompanionInteraction.None,long InteractionSerial=0)
{
    public PluginSceneState(WidgetForm form,bool visible,MediaSnapshot media,bool reducedMotion,long timestamp)
        :this(form,visible,media,reducedMotion,timestamp,CompanionInteraction.None,0) { }
}
public sealed record PluginManifest(string Id,string Name,string Version,int ApiVersion,string EntryAssembly,string EntryType,
    string ResourceDescription,bool RequiresRestart=false);
public sealed record PluginStatus(string Id,string Name,string Version,bool Enabled,PluginRunState State,string ResourceDescription,string? Reason=null,bool BuiltIn=false);
public interface IPluginContribution { string Id {get;} string Name {get;} }
public sealed record PluginCommand(string Id,string Name,Func<CancellationToken,ValueTask> Execute) : IPluginContribution;
public sealed record PluginContentItem(string Label,string? Value=null,string? CommandId=null);
public sealed record PluginContentPage(string Id,string Name,IReadOnlyList<PluginContentItem> Items) : IPluginContribution;
public enum PluginSettingKind { Boolean, Text, Number }
public sealed record PluginSetting(string Id,string Name,PluginSettingKind Kind,string DefaultValue,string? Description=null) : IPluginContribution;
public sealed record PluginCompanion(string Id,string Name,ICompanionAppearance Appearance) : IPluginContribution;
public sealed record PluginMaterial(string Id,string Name,string ResourceDescription) : IPluginContribution;
public enum PluginDrawKind { Rectangle,RoundedRectangle,Text,Line,Ellipse }
public sealed record PluginDrawOp(PluginDrawKind Kind,float X,float Y,float Width,float Height,uint Color,string? Text=null,float FontSize=14,float Radius=0);

/// <summary>Trusted, in-process extensions; this contract is not a security sandbox.</summary>
public interface IPluginContext
{
    string PluginId {get;}
    PluginSceneState Scene {get;}
    event Action<PluginSceneState>? SceneChanged;
    JsonElement? ReadConfiguration();
    ValueTask SaveConfigurationAsync(JsonElement configuration,CancellationToken cancellationToken=default);
    void RequestRender();
    void SubmitVisual(string contributionId,IReadOnlyList<PluginDrawOp> operations);
    void SubmitCompanion(CompanionVisualState? state);
    void SetMaterial(string materialId,bool active);
    void ReportResourceUsage(string description);
}
public interface IGlassDeskPlugin : IAsyncDisposable
{
    void Initialize(IPluginContext context);
    IEnumerable<IPluginContribution> RegisterContributions();
    ValueTask StartAsync(CancellationToken cancellationToken);
    ValueTask PauseAsync(CancellationToken cancellationToken);
    ValueTask StopAsync(CancellationToken cancellationToken);
}
