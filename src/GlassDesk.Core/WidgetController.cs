namespace GlassDesk.Core;

public readonly record struct WidgetAnchor(double CenterX,double TopY);

public sealed class WidgetController
{
    private WidgetPreferences preferences;
    private MonitorDescriptor monitor;
    private WidgetAnchor anchor;
    private double? hoverAt,leaveAt;
    private bool inside,down,dragging,manualHidden,fullscreen,fullscreenOverride;
    private int pressedControl;
    private int pressX,pressY;
    private WidgetAnchor pressAnchor;
    private nint lastExternalWindow;
    private CapsuleContentMode capsuleMode=CapsuleContentMode.Clock;
    private bool mediaConnected,companion=true;
    private float musicCapsuleWidth=256;
    private float? detailsPageHeight;
    public WidgetForm Form {get;private set;}=WidgetForm.Capsule;
    public bool Visible {get;private set;}=true;
    public bool IsDragging=>dragging;
    public WidgetAnchor Anchor=>anchor;
    public MonitorDescriptor Monitor=>monitor;
    public MotionController Motion {get;}
    public bool NeedsTick=>hoverAt.HasValue || leaveAt.HasValue || Motion.Active;
    public event Action<WidgetForm>? FormChanged;
    public event Action<bool>? VisibilityChanged;
    public event Action<WidgetPose>? PresentationChanged;
    public event Action<int>? ActionRequested;
    public event Action<WidgetAnchor,MonitorDescriptor>? PlacementCommitted;

    public WidgetController(WidgetPreferences preferences,MonitorDescriptor monitor,WidgetAnchor anchor)
    {
        this.preferences=preferences.Validated();this.monitor=monitor;this.anchor=anchor;
        ClampAnchor(); Motion=new(TargetPose());
    }
    private float Scale=>monitor.Scale>0?monitor.Scale:1;
    public WidgetLayoutMetrics Layout
    {
        get
        {
            var metrics=WidgetLayoutMetrics.Resolve(Form,Form==WidgetForm.Capsule?capsuleMode:mediaConnected?CapsuleContentMode.Music:CapsuleContentMode.Clock,companion,musicCapsuleWidth);
            if (Form == WidgetForm.Details && detailsPageHeight is float height) return metrics with { Width=360, Height=height };
            // The music Details page is a fixed 476 DIP; only real pages own a separate height.
            return metrics;
        }
    }
    /// <summary>A real non-music page owns its height independently of media connection changes.</summary>
    public void SetDetailsPageHeight(float? height, double now)
    {
        if (height.HasValue && (!float.IsFinite(height.Value) || height.Value < 160 || height.Value > 512))
            throw new ArgumentOutOfRangeException(nameof(height));
        if (detailsPageHeight == height) return;
        detailsPageHeight = height;
        if (Form != WidgetForm.Details) return;
        ClampAnchor(); Motion.Retarget(TargetPose(), now, 160, preferences.ReducedMotion);
        PresentationChanged?.Invoke(Motion.Sample(now));
    }
    public void UpdateContent(CapsuleContentMode mode,bool connected,bool hasCompanion,float measuredMusicWidth,double now,bool timeline=false)
    {
        var previous=Layout;
        capsuleMode=mode;mediaConnected=connected;companion=hasCompanion;musicCapsuleWidth=Math.Clamp(measuredMusicWidth,208,296);
        _=timeline; // Timeline availability no longer changes the Details height (fixed 476).
        if(previous==Layout)return;
        ClampAnchor();Motion.Retarget(TargetPose(),now,160,preferences.ReducedMotion);
        PresentationChanged?.Invoke(Motion.Sample(now));
    }
    private WidgetPose TargetPose()
    {
        float width=Layout.Width;
        float height=Layout.Height;
        width=Math.Min(width,monitor.WorkArea.Width/Scale);
        height=Math.Min(height,monitor.WorkArea.Height/Scale);
        float x=(float)Math.Clamp(anchor.CenterX-width*Scale/2,monitor.WorkArea.X,Math.Max(monitor.WorkArea.X,monitor.WorkArea.Right-width*Scale));
        float y=(float)Math.Clamp(anchor.TopY,monitor.WorkArea.Y,Math.Max(monitor.WorkArea.Y,monitor.WorkArea.Bottom-height*Scale));
        return new(x,y,width,height,Math.Min(Layout.Radius,height/2));
    }
    private void ClampAnchor()
    {
        double half=Math.Min(WidgetLayoutMetrics.Resolve(WidgetForm.Capsule,capsuleMode,companion,musicCapsuleWidth).Width*Scale,monitor.WorkArea.Width)/2;
        anchor=new(Math.Clamp(anchor.CenterX,monitor.WorkArea.X+half,monitor.WorkArea.Right-half),
            Math.Clamp(anchor.TopY,monitor.WorkArea.Y,Math.Max(monitor.WorkArea.Y,monitor.WorkArea.Bottom-52*Scale)));
    }
    public void ChangeForm(WidgetForm next,double now)
    {
        if (Form==next) return;
        var previous=Form; Form=next; hoverAt=leaveAt=null;
        int duration=next==WidgetForm.Capsule?180:next==WidgetForm.Peek?220:previous==WidgetForm.Capsule?280:260;
        Motion.Retarget(TargetPose(),now,duration,preferences.ReducedMotion);
        FormChanged?.Invoke(next);PresentationChanged?.Invoke(Motion.Sample(now));
    }
    public void UpdatePreferences(WidgetPreferences value,double now)
    {
        preferences=value.Validated();
        if(inside && Form==WidgetForm.Capsule && !down) hoverAt=now+preferences.HoverDelayMs;
        if(!inside && Form==WidgetForm.Peek && !down) leaveAt=now+preferences.LeaveDelayMs;
        Motion.Retarget(TargetPose(),now,120,preferences.ReducedMotion); ApplyVisibility(now);
    }
    public void SetMonitor(MonitorDescriptor next,double now,bool migrate=false)
    {
        if (next==monitor) return;
        if(migrate)
        {
            double relativeX=(anchor.CenterX-monitor.WorkArea.X)/Math.Max(1,monitor.WorkArea.Width);
            double relativeY=(anchor.TopY-monitor.WorkArea.Y)/Scale;
            anchor=new(next.WorkArea.X+relativeX*next.WorkArea.Width,next.WorkArea.Y+relativeY*next.Scale);
        }
        monitor=next;ClampAnchor();
        if(migrate) Motion.Jump(TargetPose());
        else Motion.Retarget(TargetPose(),now,120,preferences.ReducedMotion);
        PresentationChanged?.Invoke(Motion.Sample(now));
        if(migrate) PlacementCommitted?.Invoke(anchor,monitor);
    }
    public void ResetPosition(MonitorDescriptor next,double now)
    {
        monitor=next;anchor=new(next.WorkArea.X+next.WorkArea.Width/2.0,next.WorkArea.Y+24*next.Scale);
        ChangeForm(WidgetForm.Capsule,now);Motion.Retarget(TargetPose(),now,120,preferences.ReducedMotion);
        PlacementCommitted?.Invoke(anchor,monitor);PresentationChanged?.Invoke(Motion.Sample(now));
    }
    public void RestoreAnchor(WidgetAnchor value,double now)
    {
        anchor=value;ClampAnchor();Motion.Jump(TargetPose());PresentationChanged?.Invoke(Motion.Current);
    }
    private void ApplyVisibility(double now)
    {
        bool next=fullscreen&&preferences.AvoidFullscreen?fullscreenOverride:!manualHidden;
        if (Visible==next) return;
        Visible=next;hoverAt=leaveAt=null; down=dragging=false;
        if(next) ChangeForm(WidgetForm.Capsule,now);
        VisibilityChanged?.Invoke(next);
    }
    public void CollapseAfterOutsidePress(double now)
    {inside=down=dragging=false;hoverAt=leaveAt=null;ChangeForm(WidgetForm.Capsule,now);}
    public void ToggleVisibility(double now)
    {
        if(fullscreen && preferences.AvoidFullscreen)
        {
            fullscreenOverride=!fullscreenOverride;
        }
        else manualHidden=!manualHidden;
        ApplyVisibility(now);
    }
    public void ApplySystemScene(SystemScene scene,double now)
    {
        bool changedWindow=scene.ForegroundWindow!=lastExternalWindow;
        lastExternalWindow=scene.ForegroundWindow;
        if(fullscreen!=scene.Fullscreen)
        {
            fullscreen=scene.Fullscreen;fullscreenOverride=false;
            if(!fullscreen) ChangeForm(WidgetForm.Capsule,now);
        }
        else if(changedWindow && scene.Maximized && !fullscreen) ChangeForm(WidgetForm.Capsule,now);
        ApplyVisibility(now);
    }
    public void HandlePointer(HostPointerEvent e,int controlId,double now)
    {
        if(!Visible && e.Kind is not HostEventKind.Invoke) return;
        switch(e.Kind)
        {
            case HostEventKind.Enter:
                inside=true;leaveAt=null;
                if(Form==WidgetForm.Capsule && !down) hoverAt=now+preferences.HoverDelayMs;
                break;
            case HostEventKind.Leave:
                inside=false;hoverAt=null;
                if(Form==WidgetForm.Peek && !down) leaveAt=now+preferences.LeaveDelayMs;
                break;
            case HostEventKind.Down:
                down=true;dragging=false;pressedControl=controlId;pressX=e.ScreenX;pressY=e.ScreenY;pressAnchor=anchor;hoverAt=leaveAt=null;
                break;
            case HostEventKind.Move:
                UpdateBodyDrag(e.ScreenX,e.ScreenY,now);
                break;
            case HostEventKind.Up:
                if(!down) break;
                // The release position may be newer than the last delivered move.
                UpdateBodyDrag(e.ScreenX,e.ScreenY,now);
                down=false;
                if(dragging)
                {
                    dragging=false;SnapAnchor();Motion.Retarget(TargetPose(),now,120,preferences.ReducedMotion);
                    PlacementCommitted?.Invoke(anchor,monitor);
                    if(!inside && Form==WidgetForm.Peek) leaveAt=now+preferences.LeaveDelayMs;
                }
                else if(controlId==pressedControl) Invoke(controlId,now);
                break;
            case HostEventKind.CaptureLost:
                down=dragging=false;
                if(!inside && Form==WidgetForm.Peek) leaveAt=now+preferences.LeaveDelayMs;
                break;
            case HostEventKind.Invoke: Invoke(controlId,now);break;
        }
    }
    private void UpdateBodyDrag(int screenX,int screenY,double now)
    {
        if(!down || pressedControl!=WidgetActions.Body) return;
        double dx=screenX-pressX,dy=screenY-pressY;
        if(dx*dx+dy*dy>36*Scale*Scale) dragging=true;
        if(!dragging) return;
        var previous=anchor;anchor=new(pressAnchor.CenterX+dx,pressAnchor.TopY+dy);
        if(anchor==previous) return;
        Motion.Translate((float)(anchor.CenterX-previous.CenterX),(float)(anchor.TopY-previous.TopY),now);
        PresentationChanged?.Invoke(Motion.Sample(now));
    }
    private void Invoke(int control,double now)
    {
        if(control==WidgetActions.Body) ChangeForm(WidgetForm.Details,now);
        else if(control==WidgetActions.Close) ChangeForm(WidgetForm.Capsule,now);
        else ActionRequested?.Invoke(control);
    }
    private void SnapAnchor()
    {
        float width=Layout.Width;
        float height=Layout.Height;
        double distance=12*Scale;
        double center=anchor.CenterX,top=anchor.TopY;
        if(Math.Abs(center-(monitor.WorkArea.X+monitor.WorkArea.Width/2.0))<=distance) center=monitor.WorkArea.X+monitor.WorkArea.Width/2.0;
        else if(Math.Abs(center-width*Scale/2-monitor.WorkArea.X)<=distance) center=monitor.WorkArea.X+width*Scale/2;
        else if(Math.Abs(center+width*Scale/2-monitor.WorkArea.Right)<=distance) center=monitor.WorkArea.Right-width*Scale/2;
        if(Math.Abs(top-monitor.WorkArea.Y)<=distance) top=monitor.WorkArea.Y;
        else if(Math.Abs(top+height*Scale-monitor.WorkArea.Bottom)<=distance) top=monitor.WorkArea.Bottom-height*Scale;
        anchor=new(center,top);ClampAnchor();
    }
    public void Tick(double now)
    {
        if(hoverAt is double hover && now>=hover) {hoverAt=null;if(inside&&!down)ChangeForm(WidgetForm.Peek,now);}
        if(leaveAt is double leave && now>=leave) {leaveAt=null;if(!inside&&!down)ChangeForm(WidgetForm.Capsule,now);}
        if(Motion.Active) PresentationChanged?.Invoke(Motion.Sample(now));
    }
}
