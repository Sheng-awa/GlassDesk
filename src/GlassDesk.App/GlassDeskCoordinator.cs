using System.Diagnostics;
using GlassDesk.Core;
using GlassDesk.NativeBridge;
using GlassDesk.Platform;
using Microsoft.UI.Dispatching;
using Windows.System.Power;
using Windows.UI.ViewManagement;
using GlassDesk.Plugins;
using System.Text.Json;

namespace GlassDesk.App;

internal sealed partial class GlassDeskCoordinator : ISettingsCoordinator,IDisposable
{
    readonly LaunchOptions options;
    readonly Action exit;
    readonly DispatcherQueue queue=DispatcherQueue.GetForCurrentThread();
    readonly UiDispatcher dispatcher;
    readonly PreferenceStore store;
    readonly Diagnostics diagnostics=new();
    readonly SemaphoreSlim preferenceGate=new(1,1);
    readonly NativeWidgetHost host;
    readonly WidgetScene scene;
    readonly WidgetController controller;
    readonly SystemIntegration system;
    readonly ForegroundObserver foreground;
    readonly OutsidePointerObserver outside;
    long openSequence, geometryVersion;
    readonly DispatcherQueueTimer companionIdleTimer;
    WidgetPreferences? appearancePreview;
    readonly NetEaseMediaAdapter media;
    readonly ProcessAudioVisualizationSource audio;
    readonly PluginHost plugins;
    readonly CapsuleContentPolicy contentPolicy=new();
    readonly DispatcherQueueTimer motionTimer,clockTimer,timelineTimer,graphicsRetryTimer,pauseTimer,materialSnapshotTimer,cursorTimer,textToneTimer;
    HostContext? textToneContext;
    nint textToneForeground;
    bool lightText;
    WidgetReadabilityDecision readability = new(false,true,true,0,"Awaiting an advisory background observation; user material settings apply.");
    WidgetReadabilityDecision clockReadability = new(false,true,true,0,"Awaiting a clock-plate observation.");
    (WidgetForm Form,bool Connected,float Tint,bool Liquid,bool Time,bool Date,bool Weekday)? readabilityMaterial;
    int materialSnapshotAttempts;
    readonly UISettings uiSettings=new();
    readonly Stopwatch uptime=Stopwatch.StartNew();
    readonly MonitorService monitorService=new();
    IReadOnlyList<MonitorDescriptor> monitors;
    SettingsWindow? settings;
    ResidencyProbe? qa;
    QaSnapshotRequests? snapshotRequests;
    bool disposed,quitting,solid,seeking,applyingHost,nativeRecoveryPending;
    bool effectsSubscribed,animationsSubscribed,powerSubscribed;
    string seekToken="";
    string? notice;
    uint previousDpi;
    int graphicsRetries;
    bool pointerDown;
    int pressedAction=-1;
    CompanionInteraction companionInteraction;
    long companionInteractionSerial;
    public WidgetPreferences Preferences {get;private set;}
    public MediaSnapshot Media=>media.Current;
    public string Status=>notice??(Media.Connected?(Media.Timeline?.Valid==true?"已连接网易云音乐。":"已连接网易云音乐；当前客户端未提供播放时间轴。") :"未连接网易云音乐，当前显示本地时间。");
    public string? ConnectionIssue=>notice??Media.Notice;
    public bool IsWidgetVisible=>controller.Visible;
    public bool IsUiPreview=>options.UiPreview;
    public IReadOnlyList<PluginStatus> Plugins=>plugins.Statuses;
    public IReadOnlyList<PluginContentPage> PluginPages=>plugins.Pages;
    public string AudioStatus=>audio.Current.Reason??audio.Current.State.ToString();
    public string LiquidGlassStatus=>host.LiquidGlassStatus;
    public event Action? Changed;
    double Now=>uptime.Elapsed.TotalMilliseconds;
    WidgetPreferences EffectivePreferences=>Preferences with {CompanionColor=appearancePreview?.CompanionColor??Preferences.CompanionColor,CompanionAppearanceId=appearancePreview?.CompanionAppearanceId??Preferences.CompanionAppearanceId,ReducedMotion=Preferences.ReducedMotion||!uiSettings.AnimationsEnabled||solid};

    public GlassDeskCoordinator(LaunchOptions options,Action exit)
    {
        this.options=options;this.exit=exit;dispatcher=new(queue);store=new(options.DataDirectory);
        Preferences=store.Load();notice=store.TakeLoadNotice();monitors=monitorService.GetMonitors();
        pages=new(Preferences);
        var monitor=monitors.FirstOrDefault(m=>m.Id==Preferences.MonitorId)??monitors.First(m=>m.Primary);
        var anchor=new WidgetAnchor(monitor.WorkArea.X+Preferences.AnchorX*monitor.WorkArea.Width,monitor.WorkArea.Y+Preferences.AnchorY*monitor.Scale);
        controller=new(Preferences,monitor,anchor);
        host=new NativeWidgetHost();scene=new(host){CaptureTypographyDiagnostics=options.QaReport is not null};previousDpi=host.Context.Dpi;
        scene.RenderingFailed+=error=>RecoverGraphics(error,"artwork-transition");
        if(options.UiPreview)
        {
            string version=typeof(GlassDeskCoordinator).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute),false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]??"preview";
            host.SetTitle($"GlassDesk UI Preview · {version}");
        }
        system=new(host.Handle,dispatcher,diagnostics,options.Identity);foreground=new(dispatcher);media=new(dispatcher,diagnostics);
        audio=new(dispatcher);plugins=new(dispatcher,store.DirectoryPath);
        outside=new(dispatcher,press=>
        {
            if(disposed||quitting||!OutsidePointerSnapshot.BelongsToVisibleDetails(press,openSequence,controller.Visible,controller.Form))return;
            pointerDown=false;pressedAction=-1;seeking=false;
            CommitBoardOnLeave();
            scene.SetPointerFeedback(-1);scene.SetCapture(PointerCaptureOwner.None,Now);
            controller.CollapseAfterOutsidePress(Now);WakeMotion();
        },error=>diagnostics.RecordError("outside-pointer",error));
        companionIdleTimer=queue.CreateTimer();companionIdleTimer.IsRepeating=false;
        companionIdleTimer.Tick+=(_,_)=>{if(!disposed){TryScene(()=>scene.TickCompanion(Now));WakeMotion();}};
        audio.FrameChanged+=frame=>{if(!disposed)TryScene(()=>scene.UpdateAudio(frame));};
        plugins.CompanionChanged+=state=>{if(!disposed)TryScene(()=>{scene.SetMonotonicTime(Now);scene.SetCompanionState(state);WakeMotion();});};
        plugins.VisualsChanged+=visuals=>{if(!disposed)TryScene(()=>scene.SetPluginVisuals(visuals));};
        plugins.Changed+=()=>{if(!disposed){TryScene(()=>scene.SetPluginPages(plugins.Pages));Render();Changed?.Invoke();}};
        motionTimer=queue.CreateTimer();motionTimer.Interval=TimeSpan.FromMilliseconds(16);motionTimer.Tick+=(_,_)=>{controller.Tick(Now);TryScene(()=>scene.TickCompanion(Now));if(!controller.NeedsTick&&!scene.SceneMotionActive){motionTimer.Stop();ScheduleCompanionIdle();}UpdateOutsideGeometry();};
        clockTimer=queue.CreateTimer();clockTimer.IsRepeating=false;clockTimer.Tick+=(_,_)=>Render();
        cursorTimer=queue.CreateTimer();cursorTimer.Interval=TimeSpan.FromMilliseconds(33);cursorTimer.Tick+=(_,_)=>UpdateCursorGaze();
        textToneTimer=queue.CreateTimer();textToneTimer.IsRepeating=false;textToneTimer.Interval=TimeSpan.FromMilliseconds(220);
        textToneTimer.Tick+=(_,_)=>
        {
            if(disposed||quitting||!controller.Visible)return;
            var next=WidgetTextToneProbe.ReadPolicy(host.Context,scene.TextBackgroundY,Preferences.TintOpacity,
                false,lightText,scene.BodyUsesLargeClock?3:4.5,scene.BodyHasRequiredSecondary);
            clockReadability=controller.Form==WidgetForm.Peek
                ?WidgetTextToneProbe.ReadPolicy(host.Context,scene.ClockPlateBackgroundY,Preferences.TintOpacity,true,clockReadability.LightInk,
                    scene.ClockPlateUsesLargeClock?3:4.5,scene.ClockPlateHasRequiredSecondary)
                :next;
            readability=next;lightText=next.LightInk;
            bool textChanged=ApplySceneInk();
            if(textChanged)Render();
        };
        pauseTimer=queue.CreateTimer();pauseTimer.IsRepeating=false;pauseTimer.Tick+=(_,_)=>{if(contentPolicy.Tick(Now)){Render();WakeMotion();}UpdateContentTimers();};
        timelineTimer=queue.CreateTimer();timelineTimer.Interval=TimeSpan.FromMilliseconds(250);timelineTimer.Tick+=(_,_)=>TryScene(()=>scene.UpdateTimeline(Media,Stopwatch.GetTimestamp()));
        graphicsRetryTimer=queue.CreateTimer();graphicsRetryTimer.Interval=TimeSpan.FromSeconds(2);graphicsRetryTimer.IsRepeating=false;graphicsRetryTimer.Tick+=(_,_)=>Render();
        materialSnapshotTimer=queue.CreateTimer();materialSnapshotTimer.Interval=TimeSpan.FromSeconds(1);materialSnapshotTimer.IsRepeating=false;
        materialSnapshotTimer.Tick+=(_,_)=>
        {
            if(disposed||options.QaMaterialFrame is null)return;
            try{host.SaveLiquidGlassDiagnosticFrame(options.QaMaterialFrame);}
            catch(Exception error){diagnostics.RecordError("qa-material",error);if(++materialSnapshotAttempts<5)materialSnapshotTimer.Start();}
        };
        plugins.MaterialChanged+=(id,active)=>{if(!disposed&&id=="liquidglass"){try{host.SetLiquidGlassEnabled(active);if(active&&options.QaMaterialFrame is not null){materialSnapshotAttempts=0;materialSnapshotTimer.Start();}}catch(Exception error){diagnostics.RecordError("liquidglass",error);}Changed?.Invoke();}};
        controller.FormChanged+=form=>
        {
            if(form!=WidgetForm.Details)CommitBoardOnLeave();
            Render();
            if(form==WidgetForm.Details)scene.BeginDetails(++openSequence,Now);else scene.EndDetails();
            UpdateOutsideGeometry();UpdatePlugins();WakeMotion();
        };
        controller.VisibilityChanged+=visible=>{if(!visible)CommitBoardOnLeave();system.WidgetVisible=visible;host.SetVisible(visible);scene.SetSceneVisible(visible,Now);UpdateOutsideGeometry();contentPolicy.Tick(Now);Render();if(visible)ScheduleTextTone();else textToneTimer.Stop();UpdatePlugins();Changed?.Invoke();};
        controller.PresentationChanged+=ApplyPresentation;
        controller.ActionRequested+=InvokeAction;
        controller.PlacementCommitted+=OnPlacementCommitted;
        host.PointerEvent+=OnPointer;
        host.WindowContextChanged+=OnWindowContext;
        host.NativeError+=_=>
        {
            if(disposed||quitting||nativeRecoveryPending)return;
            nativeRecoveryPending=true;
            if(!queue.TryEnqueue(()=>
            {
                nativeRecoveryPending=false;
                if(!disposed&&!quitting)RecoverGraphics(new System.Runtime.InteropServices.COMException("Native graphics update failed."),"native");
            }))nativeRecoveryPending=false;
        };
        media.SnapshotChanged+=_=>{if(seeking&&seekToken!=Media.SessionToken){seeking=false;TryScene(()=>scene.SetSeekPreview(null));}contentPolicy.Update(Media,Now);Render();UpdatePlugins();Changed?.Invoke();};
        system.ShowHideRequested+=ToggleVisibility;system.SettingsRequested+=ShowSettings;system.OpenMusicRequested+=OpenMusic;
        system.CollapseRequested+=()=>{controller.CollapseAfterOutsidePress(Now);WakeMotion();};system.ResetPositionRequested+=ResetPosition;system.QuitRequested+=Quit;
        system.DisplayChanged+=RefreshMonitors;system.Resumed+=OnResume;system.SystemSettingsChanged+=RefreshSystemSettings;
        foreground.SceneChanged+=state=>{controller.ApplySystemScene(state,Now);if(textToneForeground!=state.ForegroundWindow){textToneForeground=state.ForegroundWindow;ScheduleTextTone();}WakeMotion();};
        // AccessibilitySettings.HighContrastChanged is unsupported in desktop apps.
        // The owned HWND receives WM_SETTINGCHANGE/WM_SYSCOLORCHANGE instead.
        effectsSubscribed=TrySubscribe(()=>uiSettings.AdvancedEffectsEnabledChanged+=OnAdvancedEffects);
        animationsSubscribed=TrySubscribe(()=>uiSettings.AnimationsEnabledChanged+=OnAnimations);
        powerSubscribed=TrySubscribe(()=>PowerManager.EnergySaverStatusChanged+=OnPower);
        InitializeWidgetBoard();
        RefreshSystemSettings();ApplyPresentation(controller.Motion.Current);Render();host.SetVisible(true);
        if(!options.UiPreview&&Preferences.HotkeyKey!=0 && !system.TrySetHotkey(Preferences.HotkeyModifiers,Preferences.HotkeyKey,out var error))notice=error;
        diagnostics.RecordEvent("app","started");
    }
    public async void Start()
    {
        try
        {
            foreground.Start();foreground.Refresh();
            widgetLoadTask=LoadWidgetBoardAsync();await widgetLoadTask;
            await media.StartAsync();
            contentPolicy.Update(Media,Now);
            await plugins.ApplyPreferencesAsync(Preferences);
            await plugins.UpdateSceneAsync(PluginScene());
            Render();
            if(disposed)return;
            if(options.QaSnapshotRequest is { } snapshotRequest)snapshotRequests=new(host,queue,snapshotRequest);
            if(!options.NoSettings && !Preferences.OnboardingSeen)
            {
                ShowSettings();
            }
        }
        catch(Exception error) {diagnostics.RecordError("start",error);notice="连接初始化失败，可以在设置中查看状态。";Render();Changed?.Invoke();}
        if(!disposed && options.QaReport is not null)
        {
            try { qa=new(queue,controller,()=>Now,WakeMotion,options.QaReport,options.QaCycles,scene.GetTypographyDiagnostics,()=>new {Body=readability,ClockPlate=clockReadability});qa.RecordContent(contentPolicy.Mode,contentPolicy.ForForm(controller.Form),Media.State,controller.Form,controller.Visible); }
            catch(Exception error) { diagnostics.RecordError("qa-start",error); }
        }
    }
    void WakeMotion(){if(disposed)return;if(controller.NeedsTick||scene.SceneMotionActive){companionIdleTimer.Stop();if(!motionTimer.IsRunning)motionTimer.Start();}else ScheduleCompanionIdle();}
    void ScheduleCompanionIdle()
    {
        companionIdleTimer.Stop();
        if(controller.Visible&&scene.NextCompanionWake is double wake){companionIdleTimer.Interval=TimeSpan.FromMilliseconds(Math.Max(1,wake-Now));companionIdleTimer.Start();}
    }
    void UpdateOutsideGeometry()
    {
        if(disposed||quitting||controller.Form!=WidgetForm.Details||!controller.Visible){outside.Update(null);return;}
        var context=host.Context;float scale=context.Dpi/96f;
        outside.Update(new(openSequence,++geometryVersion,new(context.X,context.Y,context.Width*scale,context.Height*scale),controller.Motion.Current.Radius*scale,scene.CaptureOwner,OwnedWidgetRegions()));
    }
    public void PreviewCompanionAppearance(uint color,string appearanceId)
    {
        scene.SetMonotonicTime(Now);
        appearancePreview=Preferences with {CompanionColor=color|0xff000000,CompanionAppearanceId=CompanionAppearanceIds.Normalize(appearanceId)};
        plugins.PreviewBuiltinAppearance(color,appearanceId);
        if(plugins.IsBuiltinCompanionActive)scene.PreviewCompanionAppearance(color,appearanceId);
        Render();
    }
    PluginSceneState PluginScene()=>new(controller.Form,controller.Visible,Media,EffectivePreferences.ReducedMotion,Stopwatch.GetTimestamp(),companionInteraction,companionInteractionSerial);
    void CompanionReact(CompanionInteraction interaction)
    {
        companionInteraction=interaction;companionInteractionSerial++;UpdatePlugins();
    }
    async void UpdatePlugins()
    {
        if(disposed||quitting)return;
        try{await plugins.UpdateSceneAsync(PluginScene());}
        catch(Exception error){if(!disposed)diagnostics.RecordError("plugin-scene",error);}
    }
    void ApplyPresentation(WidgetPose pose)
    {
        if(disposed||applyingHost)return;
        applyingHost=true;
        try
        {
            host.ApplyFrame(new(pose.Width,pose.Height,pose.Radius,Preferences.Blur,Preferences.TintOpacity,solid));
            host.SetPlacement((int)Math.Round(pose.X),(int)Math.Round(pose.Y));
            scene.ApplyPresentation(pose);UpdateOutsideGeometry();
            if(textToneContext!=host.Context){textToneContext=host.Context;ScheduleTextTone();}
        }
        catch(Exception error){diagnostics.RecordError("graphics",error);notice="图形暂时不可用，正在使用简化显示。";solid=true;}
        finally{applyingHost=false;}
    }
    void ScheduleTextTone()
    {
        if(disposed||quitting||!controller.Visible)return;
        textToneTimer.Stop();textToneTimer.Start();
    }
    bool ApplySceneInk()=>scene.SetTextTone(!solid&&lightText,!solid&&readability.PromoteSecondary,
        !solid&&clockReadability.LightInk,!solid&&clockReadability.PromoteSecondary);
    void Render()
    {
        if(disposed)return;
        try
        {
            contentPolicy.Tick(Now);
            scene.SetMonotonicTime(Now);
            ApplySceneInk();
            PrepareWidgetBoard();
            controller.UpdateContent(contentPolicy.Mode,Media.Connected,Preferences.CompanionEnabled,scene.MeasureMusicCapsuleWidth(Media,Preferences),Now,Media.Timeline?.Valid==true);
            var material=(controller.Form,Media.Connected,Preferences.TintOpacity,Preferences.LiquidGlassEnabled,
                Preferences.ClockShowTime,Preferences.ClockShowDate,Preferences.ClockShowWeekday);
            if(readabilityMaterial!=material)
            {readabilityMaterial=material;ScheduleTextTone();}
            scene.SetDetailsViewport(Math.Min(controller.Layout.Height,controller.Monitor.WorkArea.Height/controller.Monitor.Scale),Math.Min(360,controller.Monitor.WorkArea.Width/controller.Monitor.Scale));
            scene.Render(controller.Form,Media,DateTimeOffset.Now,EffectivePreferences,notice,contentPolicy.Mode);
            ReconcileWidgetBoard();
            qa?.RecordContent(contentPolicy.Mode,contentPolicy.ForForm(controller.Form),Media.State,controller.Form,controller.Visible);
            WakeMotion();
            graphicsRetries=0;
        }
        catch(Exception error)
        {
            RecoverGraphics(error,"graphics-render");
            return;
        }
        UpdateContentTimers();
    }
    bool TryScene(Action action)
    {
        if(disposed)return false;
        try{action();return true;}
        catch(Exception error){RecoverGraphics(error,"graphics-update");return false;}
    }
    void RecoverGraphics(Exception error,string component)
    {
        diagnostics.RecordError(component,error);notice="图形暂时不可用，已切换简化材质并尝试恢复。";solid=true;
        timelineTimer.Stop();clockTimer.Stop();cursorTimer.Stop();
        ApplyPresentation(controller.Motion.Sample(Now));
        if(graphicsRetries<3&&!graphicsRetryTimer.IsRunning){graphicsRetries++;graphicsRetryTimer.Start();}
        Changed?.Invoke();
    }
    void UpdateContentTimers()
    {
        bool clock=controller.Visible&&(controller.Form==WidgetForm.Peek||controller.Form==WidgetForm.Capsule&&contentPolicy.ForForm(controller.Form)==CapsuleContentMode.Clock);
        clockTimer.Stop();
        if(clock){var current=DateTimeOffset.Now;clockTimer.Interval=TimeSpan.FromMilliseconds(Math.Max(10,60000-current.Second*1000-current.Millisecond));clockTimer.Start();}
        pauseTimer.Stop();
        if(controller.Visible&&contentPolicy.NeedsTick){pauseTimer.Interval=TimeSpan.FromMilliseconds(Math.Max(1,contentPolicy.RemainingDelay(Now)));pauseTimer.Start();}
        bool waveform=controller.Visible&&!(controller.Form==WidgetForm.Details&&pages.CurrentPageId==DetailsPageController.Widgets)&&contentPolicy.ForForm(controller.Form)==CapsuleContentMode.Music&&Preferences.AudioVisualizationEnabled;
        audio.SetTarget(Media,waveform);
        bool progress=controller.Visible&&controller.Form==WidgetForm.Details&&pages.CurrentPageId==DetailsPageController.Music&&Media.Timeline?.Valid==true&&Media.State==PlaybackState.Playing;
        if(progress&&!timelineTimer.IsRunning)timelineTimer.Start();else if(!progress)timelineTimer.Stop();
        bool follow=controller.Visible&&controller.Form==WidgetForm.Capsule&&Preferences.CompanionEnabled&&Preferences.CompanionFollowMouse&&!EffectivePreferences.ReducedMotion;
        if(follow)
        {
            if(!cursorTimer.IsRunning){UpdateCursorGaze();cursorTimer.Start();}
        }
        else
        {
            cursorTimer.Stop();TryScene(()=>scene.ClearCompanionPointerGaze());
        }
    }
    void UpdateCursorGaze()
    {
        if(disposed||quitting)return;
        if(CursorPositionSource.TryGetRelativePosition(host.Context,out var x,out var y))
            TryScene(()=>scene.SetCompanionPointerGaze(x,y));
        else TryScene(()=>scene.ClearCompanionPointerGaze());
    }
    void OnPointer(HostPointerEvent e)
    {
        if(disposed||quitting)return;
        int id=e.Kind is HostEventKind.Invoke or HostEventKind.RangeValue?e.ControlId:scene.HitTest(e.X,e.Y);
        if(e.Kind is HostEventKind.Enter or HostEventKind.Move or HostEventKind.Leave or HostEventKind.CaptureLost)
            TryScene(()=>scene.UpdateCompanionGaze(e));
        if(e.Kind==HostEventKind.Enter)CompanionReact(CompanionInteraction.Attention);
        else if(e.Kind==HostEventKind.Down)CompanionReact(CompanionInteraction.Press);
        else if(e.Kind==HostEventKind.Up)CompanionReact(CompanionInteraction.Release);
        else if(e.Kind==HostEventKind.CaptureLost)CompanionReact(CompanionInteraction.None);
        qa?.RecordPointer(e,id,host.Context);
        if(scene.RouteSpecialPointer(e,id,Now)){UpdateOutsideGeometry();WakeMotion();return;}
        if(RouteBoardPointer(e,id))return;
        if(e.Kind==HostEventKind.Down)scene.SetCapture(id==WidgetActions.Seek?PointerCaptureOwner.Timeline:PointerCaptureOwner.WidgetDrag,Now);
        if(e.Kind is HostEventKind.Up or HostEventKind.CaptureLost)scene.SetCapture(PointerCaptureOwner.None,Now);
        UpdateOutsideGeometry();
        if(e.Kind==HostEventKind.Wheel)
        {
            if(scene.BoardHiddenPanelOpen){ScrollHiddenPanel(-(float)e.Value*40);return;}
            TryScene(()=>scene.ScrollDetails(-(float)e.Value*40));pages.SetScrollOffset(scene.CurrentPageScroll);return;
        }
        if(e.Kind==HostEventKind.RangeValue)
        {
            var control=scene.GetAccessibleControls().FirstOrDefault(c=>c.Id==WidgetActions.Seek);
            if(control is {Enabled:true}) SubmitSeek(scene.SeekPositionFor(control.X+control.Width*(float)Math.Clamp(e.Value/100,0,1)),Media.SessionToken);
            return;
        }
        if(e.Kind==HostEventKind.Down)
        {
            pointerDown=true;pressedAction=id;
            TryScene(()=>scene.SetPointerFeedback(id,true));
            if(id==WidgetActions.Seek && Media.Capabilities.Seek && Media.Timeline?.Valid==true)
            {seeking=true;seekToken=Media.SessionToken;TryScene(()=>scene.SetSeekPreview(scene.SeekPositionFor(e.X)));}
        }
        else if(e.Kind==HostEventKind.Move)
        {
            if(seeking)TryScene(()=>scene.SetSeekPreview(scene.SeekPositionFor(e.X)));else TryScene(()=>scene.SetPointerFeedback(id,pointerDown&&pressedAction==id));
            if(controller.IsDragging)
            {
                var pointerMonitor=monitors.FirstOrDefault(m=>e.ScreenX>=m.Bounds.X&&e.ScreenX<m.Bounds.Right&&e.ScreenY>=m.Bounds.Y&&e.ScreenY<m.Bounds.Bottom);
                if(pointerMonitor is not null && pointerMonitor.Id!=controller.Monitor.Id)controller.SetMonitor(pointerMonitor,Now);
            }
        }
        else if(e.Kind==HostEventKind.Up)
        {
            pointerDown=false;pressedAction=-1;
            TryScene(()=>scene.SetPointerFeedback(id,false));
            if(seeking){seeking=false;var position=scene.SeekPositionFor(e.X);TryScene(()=>scene.SetSeekPreview(null));SubmitSeek(position,seekToken);}
        }
        else if(e.Kind is HostEventKind.Leave or HostEventKind.CaptureLost)
        {TryScene(()=>scene.SetPointerFeedback(-1,false));if(e.Kind==HostEventKind.CaptureLost){pointerDown=false;pressedAction=-1;seeking=false;TryScene(()=>scene.SetSeekPreview(null));}}
        else if(e.Kind==HostEventKind.OpenSettings){ShowSettings();return;}
        else if(e.Kind==HostEventKind.Quit){Quit();return;}
        bool wasDragging=controller.IsDragging;
        controller.HandlePointer(e,id,Now);
        if(!wasDragging&&controller.IsDragging)CompanionReact(CompanionInteraction.Drag);
        WakeMotion();
    }
    async void SubmitSeek(TimeSpan? position,string token)
    {
        if(position is null||disposed)return;
        try{var result=await media.ExecuteAsync(MediaCommand.Seek,token,position);if(!result.Accepted||!result.Verified){notice=result.Error??"播放进度未得到客户端确认。";Render();Changed?.Invoke();}}
        catch(Exception error){diagnostics.RecordError("seek",error);}
    }
    async void InvokeAction(int id)
    {
        if(disposed)return;
        if(scene.BoardRender?.Actions.TryGetValue(id,out var boardAction)==true)
        {
            // UIA Invoke follows the same enabled gate as pointer input: a disabled
            // control is announced but can never execute its action.
            bool enabled=scene.BoardRender.Controls.FirstOrDefault(c=>c.Id==id) is {Enabled:true};
            if(enabled)ExecuteBoardAction(boardAction);
            return;
        }
        if(id==WidgetActions.OpenMusic){OpenMusic();return;}
        if(id==WidgetActions.Settings||id==WidgetActions.PluginTools){ShowSettings();if(id==WidgetActions.PluginTools)settings?.ShowPlugins();return;}
        MediaCommand? command=id switch {WidgetActions.Previous=>MediaCommand.Previous,WidgetActions.Toggle=>MediaCommand.TogglePlayPause,WidgetActions.Next=>MediaCommand.Next,_=>null};
        if(command is null)return;
        TryScene(()=>scene.AnimateTransportActivation(id));
        if(id is WidgetActions.Previous or WidgetActions.Next)TryScene(()=>scene.PrepareTrackTransition(id));
        try
        {
            var result=await media.ExecuteAsync(command.Value,Media.SessionToken);
            notice=result.Accepted&&result.Verified?null:result.Error??"客户端未确认此次操作。";Render();Changed?.Invoke();
        }
        catch(Exception error){diagnostics.RecordError("command",error);notice="音乐操作暂时不可用。";Render();Changed?.Invoke();}
    }
    void OnWindowContext(HostContext context)
    {
        if(disposed||context.Dpi==previousDpi)return;
        previousDpi=context.Dpi;
        queue.TryEnqueue(()=>{if(!disposed)RefreshMonitors();});
    }
    void RefreshMonitors()
    {
        if(disposed)return;
        monitors=monitorService.GetMonitors();
        var current=monitors.FirstOrDefault(m=>m.Id==controller.Monitor.Id);
        if(current is null)controller.SetMonitor(monitors.First(m=>m.Primary),Now,true);else controller.SetMonitor(current,Now);
        Render();WakeMotion();foreground.Refresh();
    }
    void OnResume(){if(disposed)return;widgetScheduler.Invalidate(DateTimeOffset.UtcNow);Render();RefreshMonitors();media.Refresh();ScheduleTextTone();}
    bool TrySubscribe(Action subscribe)
    {
        try{subscribe();return true;}
        catch(System.Runtime.InteropServices.COMException error){diagnostics.RecordError("system-notification",error);return false;}
    }
    void OnAdvancedEffects(UISettings sender,object args)=>dispatcher.TryEnqueue(RefreshSystemSettings);
    void OnAnimations(UISettings sender,object args)=>dispatcher.TryEnqueue(RefreshSystemSettings);
    void OnPower(object? sender,object args)=>dispatcher.TryEnqueue(RefreshSystemSettings);
    void RefreshSystemSettings()
    {
        if(disposed)return;
        var environment=SystemIntegration.ReadMaterialEnvironment();
        widgetScheduler.Invalidate(DateTimeOffset.UtcNow);
        solid=environment.UseSolid||!uiSettings.AdvancedEffectsEnabled;
        controller.UpdatePreferences(EffectivePreferences,Now);ApplyPresentation(controller.Motion.Sample(Now));Render();WakeMotion();
        ScheduleTextTone();
    }
    async void OnPlacementCommitted(WidgetAnchor anchor,MonitorDescriptor monitor)
    {
        if(quitting||disposed)return;
        Preferences=Preferences with {MonitorId=monitor.Id,AnchorX=(anchor.CenterX-monitor.WorkArea.X)/Math.Max(1,monitor.WorkArea.Width),AnchorY=(anchor.TopY-monitor.WorkArea.Y)/monitor.Scale};
        try{await SaveCurrentPageAsync();}catch(Exception error){diagnostics.RecordError("placement-save",error);}
        Changed?.Invoke();
    }
    public async Task<SettingsUpdateResult> UpdatePreferencesAsync(WidgetPreferences value)
    {
        await preferenceGate.WaitAsync();
        try
        {
            if(disposed)return new(false,"程序正在退出。");
            var next=pages.ApplyTo(PreferenceStore.Normalize(value));var previous=Preferences;
            bool hotkeyChanged=!options.UiPreview&&(next.HotkeyKey!=previous.HotkeyKey||next.HotkeyModifiers!=previous.HotkeyModifiers);
            if(hotkeyChanged&&!system.TrySetHotkey(next.HotkeyModifiers,next.HotkeyKey,out var hotkeyError))return new(false,hotkeyError);
            if(!options.UiPreview&&next.RunAtStartup!=previous.RunAtStartup&&!system.SetStartupEnabled(next.RunAtStartup,out var startupError))
            {if(hotkeyChanged)system.TrySetHotkey(previous.HotkeyModifiers,previous.HotkeyKey,out _);return new(false,startupError);}
            try{await store.SaveAsync(next);}
            catch(Exception error)
            {
                diagnostics.RecordError("preferences-save",error);
                if(hotkeyChanged)system.TrySetHotkey(previous.HotkeyModifiers,previous.HotkeyKey,out _);
                if(!options.UiPreview&&next.RunAtStartup!=previous.RunAtStartup)system.SetStartupEnabled(previous.RunAtStartup,out _);
                return new(false,"设置未能保存，请检查数据目录是否可写。");
            }
            Preferences=pages.ApplyTo(next);
            if(appearancePreview?.CompanionColor==next.CompanionColor&&appearancePreview.CompanionAppearanceId==next.CompanionAppearanceId)appearancePreview=null;
            notice=null;controller.UpdatePreferences(EffectivePreferences,Now);await plugins.ApplyPreferencesAsync(Preferences);await plugins.UpdateSceneAsync(PluginScene());ApplyPresentation(controller.Motion.Sample(Now));Render();WakeMotion();Changed?.Invoke();return new(true);
        }
        finally{preferenceGate.Release();}
    }
    public void ShowSettings()
    {
        if(disposed)return;
        try
        {
            settings??=options.DataDirectory is null ? new(this) : new(this,false,true,
                message=>File.AppendAllText(Path.Combine(store.DirectoryPath,"settings-construction.log"),message+Environment.NewLine));
            settings.Show();
        }
        catch(Exception error)
        {
            diagnostics.RecordError("settings-open",error);
            if(options.DataDirectory is not null)
                File.WriteAllText(Path.Combine(store.DirectoryPath,"settings-open-error.txt"),$"{DateTimeOffset.UtcNow:O}\n{error}\n");
            notice="设置暂时无法打开，请稍后重试。";
            if(options.QaReport is not null)
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"qa-settings-error.txt"),$"{DateTimeOffset.UtcNow:O}\n{error.GetType().FullName}\nHRESULT=0x{error.HResult:X8}\n{error.StackTrace}");
            Render();Changed?.Invoke();
        }
    }
    public void ResetPosition(){controller.ResetPosition(monitors.First(m=>m.Primary),Now);WakeMotion();}
    public void ToggleVisibility(){controller.ToggleVisibility(Now);WakeMotion();}
    public void OpenMusic(){if(!system.OpenNetEase(out var error)){notice=error;Render();Changed?.Invoke();}}
    public async Task RetryMediaAsync()
    {
        if(disposed||quitting)return;
        notice=null;
        await media.StartAsync();
        if(disposed||quitting)return;
        media.Refresh();Render();Changed?.Invoke();
    }
    public async Task<string> ExportDiagnosticsAsync()
    {
        Directory.CreateDirectory(store.DirectoryPath);string path=Path.Combine(store.DirectoryPath,$"diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");await diagnostics.ExportAsync(path);return path;
    }
    public async Task<SettingsUpdateResult> ImportPluginAsync(string packagePath)
    {try{await plugins.ImportAsync(packagePath);Changed?.Invoke();return new(true,"插件已安装，可独立开启。");}catch(Exception error){diagnostics.RecordError("plugin-import",error);return new(false,error.Message);}}
    public async Task<SettingsUpdateResult> RemovePluginAsync(string id)
    {try{await plugins.RemoveAsync(id);var enabled=new PluginEnableMap(Preferences.PluginEnabled);enabled.Remove(id);return await UpdatePreferencesAsync(Preferences with {PluginEnabled=enabled});}catch(Exception error){diagnostics.RecordError("plugin-remove",error);return new(false,error.Message);}}
    public Task<SettingsUpdateResult> SetPluginEnabledAsync(string id,bool enabled)
    {
        if(id=="glassdesk.companion")return UpdatePreferencesAsync(Preferences with {CompanionEnabled=enabled});
        if(id=="glassdesk.liquidglass")return UpdatePreferencesAsync(Preferences with {LiquidGlassEnabled=enabled});
        var values=new PluginEnableMap(Preferences.PluginEnabled){[id]=enabled};
        return UpdatePreferencesAsync(Preferences with {PluginEnabled=values});
    }
    public async Task<SettingsUpdateResult> ExecutePluginCommandAsync(string pluginId,string commandId)
    {try{await plugins.ExecuteAsync(pluginId,commandId);return new(true);}catch(Exception error){diagnostics.RecordError("plugin-command",error);return new(false,error.Message);}}
    public JsonElement? GetPluginConfiguration(string id)=>plugins.GetConfiguration(id);
    public IReadOnlyList<PluginSetting> GetPluginSettings(string id)=>plugins.Settings(id);
    public async Task<SettingsUpdateResult> SavePluginConfigurationAsync(string id,JsonElement value)
    {try{await plugins.SaveConfigurationAsync(id,value);UpdatePlugins();return new(true);}catch(Exception error){diagnostics.RecordError("plugin-config",error);return new(false,error.Message);}}
    public async void Quit()
    {
        if(quitting||disposed)return;quitting=true;
        CommitBoardOnLeave();widgetTimer.Stop();pageSaveTimer.Stop();
        motionTimer.Stop();clockTimer.Stop();timelineTimer.Stop();graphicsRetryTimer.Stop();pauseTimer.Stop();materialSnapshotTimer.Stop();cursorTimer.Stop();textToneTimer.Stop();companionIdleTimer.Stop();outside.Update(null);
        if(settings is not null){settings.Hide();await settings.FlushPendingAsync();}
        await preferenceGate.WaitAsync();
        try{await store.SaveAsync(pages.ApplyTo(Preferences));}catch(Exception error){diagnostics.RecordError("quit-save",error);}
        finally{preferenceGate.Release();}
        await widgetSaveTail;
        await widgetLoadTask;
        if(boardReady)
            try{await widgetStore.SaveAsync(boardEditor.State);}
            catch(Exception error){diagnostics.RecordError("widget-quit-save",error);}
        await plugins.DisposeAsync();Dispose();exit();
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;motionTimer.Stop();clockTimer.Stop();timelineTimer.Stop();graphicsRetryTimer.Stop();pauseTimer.Stop();materialSnapshotTimer.Stop();cursorTimer.Stop();textToneTimer.Stop();companionIdleTimer.Stop();outside.Update(null);
        DisposeWidgetBoard();
        qa?.Dispose();snapshotRequests?.Dispose();
        if(effectsSubscribed)uiSettings.AdvancedEffectsEnabledChanged-=OnAdvancedEffects;
        if(animationsSubscribed)uiSettings.AnimationsEnabledChanged-=OnAnimations;
        if(powerSubscribed)PowerManager.EnergySaverStatusChanged-=OnPower;
        outside.Dispose();companionIdleTimer.Stop();audio.Dispose();settings?.Dispose();foreground.Dispose();media.Dispose();system.Dispose();scene.Dispose();host.Dispose();store.Dispose();preferenceGate.Dispose();
    }
}
