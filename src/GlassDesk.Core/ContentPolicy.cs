namespace GlassDesk.Core;

public enum CapsuleContentMode { Clock, Music }

/// <summary>Presentation policy uses the host's monotonic milliseconds; media remains intact.</summary>
public sealed class CapsuleContentPolicy
{
    string token="";
    double? pausedAt;
    MediaSnapshot current=MediaSnapshot.Disconnected;
    public const double PauseDelayMs=5000;
    public bool NeedsTick=>pausedAt.HasValue && Mode==CapsuleContentMode.Music;
    public double RemainingDelay(double now)=>pausedAt is double start?Math.Max(0,PauseDelayMs-(now-start)):0;
    public CapsuleContentMode Mode {get;private set;}=CapsuleContentMode.Clock;
    public void Update(MediaSnapshot snapshot,double now)
    {
        bool replaced=token!=snapshot.SessionToken;
        bool enteringPause=replaced||current.State!=PlaybackState.Paused;
        token=snapshot.SessionToken;current=snapshot;
        if(snapshot.Connected&&snapshot.State==PlaybackState.Paused)
        {if(enteringPause)pausedAt=now;}
        else pausedAt=null;
        Tick(now);
    }
    public bool Tick(double now)
    {
        var next=current.Connected && !(current.State==PlaybackState.Paused&&pausedAt is double start&&now-start>=PauseDelayMs)
            ?CapsuleContentMode.Music:CapsuleContentMode.Clock;
        bool changed=next!=Mode;Mode=next;return changed;
    }
    public CapsuleContentMode ForForm(WidgetForm form)=>form==WidgetForm.Capsule?Mode:current.Connected?CapsuleContentMode.Music:CapsuleContentMode.Clock;
}

public readonly record struct WidgetLayoutMetrics(float Width,float Height,float Radius)
{
    public static WidgetLayoutMetrics Resolve(WidgetForm form,CapsuleContentMode mode,bool companion=true,float musicCapsuleWidth=256)
        =>(form,mode) switch
        {
            (WidgetForm.Capsule,CapsuleContentMode.Clock)=>new(200,52,26),
            (WidgetForm.Capsule,_)=>new(Math.Clamp(musicCapsuleWidth,208,296),52,26),
            (WidgetForm.Peek,CapsuleContentMode.Clock)=>new(240,144,24),
            (WidgetForm.Peek,_)=>new(320,176,24),
            (WidgetForm.Details,CapsuleContentMode.Clock)=>new(360,300,24),
            _=>new(360,476,24)
        };
}

public enum AudioVisualizationState { Inactive, Starting, Capturing, Unavailable, Silent, Faulted }
public sealed record AudioVisualizationFrame(AudioVisualizationState State,string SessionToken,long Timestamp,float Rms,float[] Bands,string? Reason=null)
{
    public static AudioVisualizationFrame Inactive {get;}=new(AudioVisualizationState.Inactive,"",0,0,new float[8]);
}
public interface IAudioVisualizationSource : IDisposable
{
    AudioVisualizationFrame Current {get;}
    event Action<AudioVisualizationFrame>? FrameChanged;
    void SetTarget(MediaSnapshot media,bool active);
}
