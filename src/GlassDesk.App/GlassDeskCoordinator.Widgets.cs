using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using GlassDesk.Core;
using GlassDesk.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;

namespace GlassDesk.App;

internal sealed partial class GlassDeskCoordinator
{
    readonly DetailsPageController pages;
    readonly WidgetBoardScheduler widgetScheduler = new(); // Built-in mode until all P6 gates pass.
    readonly SemaphoreSlim widgetSaveGate = new(1,1);
    readonly Dictionary<nint,AppWindow> widgetOwnedWindows = new();
    WidgetBoardStore widgetStore = null!;
    WidgetBoardEditor boardEditor = new(WidgetBoardData.CreateDefault());
    DispatcherQueueTimer widgetTimer = null!, pageSaveTimer = null!;
    WidgetInstanceConfigurationWindow? widgetConfiguration;
    string? widgetConfigurationInstance;
    bool boardReady, boardAddMenu, widgetRefreshQueued;
    long boardRevision;
    int boardPressedAction = -1;
    string? boardDragInstance;
    Task widgetSaveTail = Task.CompletedTask;
    Task widgetLoadTask = Task.CompletedTask;

    void InitializeWidgetBoard()
    {
        widgetStore=new(store.DirectoryPath);
        widgetTimer=queue.CreateTimer();widgetTimer.IsRepeating=false;
        widgetTimer.Tick+=OnWidgetTimer;
        pageSaveTimer=queue.CreateTimer();pageSaveTimer.IsRepeating=false;pageSaveTimer.Interval=TimeSpan.FromMilliseconds(250);
        pageSaveTimer.Tick+=OnPageSaveTimer;
        scene.DetailsPageRequested+=SelectDetailsPage;
        scene.BoardViewportChanged+=OnBoardViewportChanged;
        widgetScheduler.Updated+=OnWidgetUpdated;
    }

    async Task LoadWidgetBoardAsync()
    {
        var loaded=await widgetStore.LoadAsync();
        if(disposed||quitting)return;
        boardEditor=new(loaded);boardReady=true;boardRevision++;
        notice=widgetStore.TakeLoadNotice()??notice;
    }

    void PrepareWidgetBoard()
    {
        var state=boardEditor.State;
        float width=Math.Clamp(controller.Monitor.WorkArea.Width/controller.Monitor.Scale,64,360);
        float? height=pages.CurrentPageId==DetailsPageController.Widgets
            ?Math.Clamp(WidgetBoardLayout.Pack(state,width).ContentBottom,304,512):null;
        controller.SetDetailsPageHeight(height,Now);
        scene.SetWidgetBoard(pages.CurrentPageId,state,widgetScheduler.Results,boardRevision,boardEditor.IsEditing,boardAddMenu,pages.ScrollOffset);
    }

    void ReconcileWidgetBoard()
    {
        if(widgetTimer is null)return;
        widgetTimer.Stop();
        bool visible=boardReady&&controller.Visible&&controller.Form==WidgetForm.Details&&pages.CurrentPageId==DetailsPageController.Widgets;
        var now=DateTimeOffset.UtcNow;
        widgetScheduler.SetVisible(visible,now);
        if(!visible)return;
        pages.SetScrollOffset(scene.CurrentPageScroll);
        var render=scene.BoardRender;
        var eligible=render is null?new HashSet<string>(StringComparer.Ordinal):WidgetBoardLayout.EligibleInstances(render.Packing,
            new(0,132+render.ScrollOffset,scene.CurrentSize.X,Math.Max(0,scene.CurrentSize.Y-156)));
        widgetScheduler.Reconcile(boardEditor.State,eligible,now);
        ScheduleWidgetRefresh();
    }

    void ScheduleWidgetRefresh()
    {
        widgetTimer.Stop();
        if(disposed||quitting||widgetScheduler.NextRefreshUtc is not { } due)return;
        widgetTimer.Interval=TimeSpan.FromMilliseconds(Math.Clamp((due-DateTimeOffset.UtcNow).TotalMilliseconds,10,129600000));
        widgetTimer.Start();
    }
    async void OnWidgetTimer(DispatcherQueueTimer sender,object args)
    {
        if(disposed||quitting)return;
        try { await widgetScheduler.RefreshDueAsync(DateTimeOffset.UtcNow); }
        catch(Exception error){if(!disposed)diagnostics.RecordError("widget-refresh",error);}
        if(!disposed&&!quitting)ScheduleWidgetRefresh();
    }
    void OnWidgetUpdated()
    {
        // Providers publish off the UI thread. Coalesce callbacks without retaining
        // a queued frame after disposal or rearming a hidden-page timer.
        lock(widgetScheduler)
        {
            if(disposed||quitting||widgetRefreshQueued)return;
            widgetRefreshQueued=true;
        }
        if(!queue.TryEnqueue(()=>
        {
            lock(widgetScheduler)widgetRefreshQueued=false;
            if(disposed||quitting)return;
            boardRevision++;
            if(controller.Visible&&controller.Form==WidgetForm.Details&&pages.CurrentPageId==DetailsPageController.Widgets)Render();
        }))lock(widgetScheduler)widgetRefreshQueued=false;
    }
    void OnBoardViewportChanged(){pages.SetScrollOffset(scene.CurrentPageScroll);ReconcileWidgetBoard();}

    void SelectDetailsPage(string id)
    {
        if(disposed||quitting)return;
        pages.SetScrollOffset(scene.CurrentPageScroll);
        if(!pages.Select(id))return;
        CommitBoardOnLeave();
        Preferences=pages.ApplyTo(Preferences);
        pageSaveTimer.Stop();pageSaveTimer.Start();
        Render();WakeMotion();Changed?.Invoke();
    }
    async void OnPageSaveTimer(DispatcherQueueTimer sender,object args)
    {
        try { await SaveCurrentPageAsync(); }
        catch(Exception error){if(!disposed){diagnostics.RecordError("page-save",error);notice="页面选择暂未保存，将在退出时重试。";Changed?.Invoke();}}
    }
    async Task SaveCurrentPageAsync()
    {
        await preferenceGate.WaitAsync();
        try
        {
            if(disposed)return;
            long generation=pages.Generation;
            Preferences=pages.ApplyTo(Preferences);
            await store.SaveAsync(Preferences);
            pages.MarkSaved(generation);
        }
        finally {preferenceGate.Release();}
        if(!disposed&&!quitting&&pages.SavePending){pageSaveTimer.Stop();pageSaveTimer.Start();}
    }

    void ExecuteBoardAction(WidgetBoardUiAction action)
    {
        if(disposed||quitting||!boardReady||controller.Form!=WidgetForm.Details||pages.CurrentPageId!=DetailsPageController.Widgets)return;
        try
        {
            var item=boardEditor.State.Instances.FirstOrDefault(i=>i.InstanceId==action.InstanceId);
            switch(action.Kind)
            {
                case WidgetBoardActionKind.Add: boardAddMenu=!boardAddMenu;break;
                case WidgetBoardActionKind.AddTime: AddBuiltIn(BuiltInWidgetIds.Time);break;
                case WidgetBoardActionKind.AddDate: AddBuiltIn(BuiltInWidgetIds.Date);break;
                case WidgetBoardActionKind.BeginEdit: boardEditor.Begin();boardAddMenu=false;break;
                case WidgetBoardActionKind.Commit: QueueWidgetSave(boardEditor.Commit());boardAddMenu=false;break;
                case WidgetBoardActionKind.Cancel: CloseWidgetConfiguration();boardEditor.Cancel();boardAddMenu=false;break;
                case WidgetBoardActionKind.Configure when item is not null: ShowWidgetConfiguration(item);return;
                case WidgetBoardActionKind.Remove when item is not null && boardEditor.IsEditing:
                    if(widgetConfigurationInstance==item.InstanceId)CloseWidgetConfiguration();
                    boardEditor.Remove(item.InstanceId);break;
                case WidgetBoardActionKind.ToggleSize when item is not null && boardEditor.IsEditing:
                    boardEditor.Resize(item.InstanceId,item.Size==WidgetCardSize.Small?WidgetCardSize.Medium:WidgetCardSize.Small);break;
                case WidgetBoardActionKind.DragHandle: return; // Pointer route owns the drag; never treat it as a host-window drag.
                default: return; // Third-party commands remain unavailable in built-in mode.
            }
            boardRevision++;Render();WakeMotion();
        }
        catch(Exception error){diagnostics.RecordError("widget-action",error);notice="组件操作暂时不可用。";Changed?.Invoke();}
    }
    void AddBuiltIn(string providerId)
    {
        bool editing=boardEditor.IsEditing;
        if(!editing)boardEditor.Begin();
        JsonElement settings=providerId==BuiltInWidgetIds.Time
            ?BuiltInWidgetProviders.ToJson(new TimeWidgetSettings())
            :BuiltInWidgetProviders.ToJson(new DateWidgetSettings());
        boardEditor.Add(providerId,WidgetCardSize.Small,settings);
        if(!editing)QueueWidgetSave(boardEditor.Commit());
        boardAddMenu=false;
    }
    void CommitBoardOnLeave()
    {
        if(widgetStore is null)return;
        CloseWidgetConfiguration();boardAddMenu=false;boardPressedAction=-1;boardDragInstance=null;
        if(boardEditor.IsEditing){QueueWidgetSave(boardEditor.Commit());boardRevision++;}
        if(scene.CaptureOwner==PointerCaptureOwner.WidgetBoard)scene.SetCapture(PointerCaptureOwner.None,Now);
        if(pages.SavePending&&pageSaveTimer is not null){pageSaveTimer.Stop();if(!quitting)pageSaveTimer.Start();}
    }
    void QueueWidgetSave(WidgetBoardState state) => widgetSaveTail=SaveWidgetAfterAsync(widgetSaveTail,state);
    async Task SaveWidgetAfterAsync(Task preceding,WidgetBoardState state)
    {
        await preceding;
        await widgetSaveGate.WaitAsync();
        try {await widgetStore.SaveAsync(state);}
        catch(Exception error){if(!disposed){diagnostics.RecordError("widget-save",error);notice="组件配置暂未保存，请检查数据目录。";Changed?.Invoke();}}
        finally {widgetSaveGate.Release();}
    }

    bool RouteBoardPointer(HostPointerEvent e,int id)
    {
        if(controller.Form!=WidgetForm.Details||pages.CurrentPageId!=DetailsPageController.Widgets)return false;
        var actions=scene.BoardRender?.Actions;
        bool target=actions?.ContainsKey(id)==true;
        if(e.Kind==HostEventKind.Invoke&&target){ExecuteBoardAction(actions![id]);return true;}
        if(e.Kind==HostEventKind.Down&&target)
        {
            boardPressedAction=id;
            if(actions![id] is {Kind:WidgetBoardActionKind.DragHandle,InstanceId:{ } instance} && boardEditor.IsEditing)
            {boardDragInstance=instance;boardEditor.PointerDown(instance,new(e.X,e.Y));}
            return true;
        }
        if(boardPressedAction<0)return false;
        if(e.Kind==HostEventKind.Move&&boardDragInstance is not null)
        {
            if(boardEditor.PointerMove(new(e.X,e.Y)))scene.SetCapture(PointerCaptureOwner.WidgetBoard,Now);
            UpdateOutsideGeometry();return true;
        }
        if(e.Kind==HostEventKind.Up)
        {
            int pressed=boardPressedAction;boardPressedAction=-1;
            if(boardDragInstance is not null)
            {
                int? index=BoardDropIndex(e.X,e.Y);
                var outcome=boardEditor.PointerUp(new(e.X,e.Y),new(0,132,scene.CurrentSize.X,Math.Max(0,scene.CurrentSize.Y-156)),index);
                boardDragInstance=null;scene.SetCapture(PointerCaptureOwner.None,Now);UpdateOutsideGeometry();
                if(outcome!=WidgetDragRelease.NoDrag){boardRevision++;Render();WakeMotion();}
            }
            else if(pressed==id&&actions?.TryGetValue(id,out var action)==true)ExecuteBoardAction(action);
            return true;
        }
        if(e.Kind==HostEventKind.CaptureLost)
        {boardEditor.CancelDrag();boardDragInstance=null;boardPressedAction=-1;scene.SetCapture(PointerCaptureOwner.None,Now);UpdateOutsideGeometry();return true;}
        return true;
    }
    int? BoardDropIndex(float x,float y)
    {
        var cards=scene.BoardRender?.Packing.Cards;
        if(cards is null||cards.Count==0)return null;
        Vector2 p=new(x,y+scene.CurrentPageScroll);
        return cards.Select((c,i)=>(Index:i,Distance:Vector2.DistanceSquared(p,new(c.Bounds.X+c.Bounds.Width/2,c.Bounds.Y+c.Bounds.Height/2))))
            .OrderBy(c=>c.Distance).First().Index;
    }

    void ShowWidgetConfiguration(WidgetInstance instance)
    {
        CloseWidgetConfiguration();
        WidgetInstanceConfigurationWindow? child=null;
        child=new(instance,settings=>
        {
            bool editing=boardEditor.IsEditing;
            if(!editing)boardEditor.Begin();
            boardEditor.Configure(instance.InstanceId,settings);
            if(!editing)QueueWidgetSave(boardEditor.Commit());
            boardRevision++;Render();
        },()=>
        {
            if(child is null)return;
            TrackWidgetOwnedWindow(child.Handle,false);
            if(ReferenceEquals(widgetConfiguration,child)){widgetConfiguration=null;widgetConfigurationInstance=null;}
        });
        widgetConfiguration=child;widgetConfigurationInstance=instance.InstanceId;
        try
        {
            SetWindowLongPtr(child.Handle,-8,host.Handle);
            child.PopupOwnershipChanged+=TrackWidgetOwnedWindow;
            TrackWidgetOwnedWindow(child.Handle,true);
            child.Show();UpdateOutsideGeometry();
        }
        catch {CloseWidgetConfiguration();throw;}
    }
    void CloseWidgetConfiguration()
    {
        var child=widgetConfiguration;widgetConfiguration=null;widgetConfigurationInstance=null;
        if(child is null)return;
        child.Dispose();child.PopupOwnershipChanged-=TrackWidgetOwnedWindow;
        TrackWidgetOwnedWindow(child.Handle,false);
    }
    void TrackWidgetOwnedWindow(nint hwnd,bool owned)
    {
        if(owned)
        {
            if(widgetOwnedWindows.ContainsKey(hwnd))return;
            var app=AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            widgetOwnedWindows.Add(hwnd,app);app.Changed+=OnWidgetOwnedChanged;
        }
        else if(widgetOwnedWindows.Remove(hwnd,out var app))app.Changed-=OnWidgetOwnedChanged;
        UpdateOutsideGeometry();
    }
    void OnWidgetOwnedChanged(AppWindow sender,AppWindowChangedEventArgs args)=>UpdateOutsideGeometry();
    IEnumerable<FloatRect> OwnedWidgetRegions()
    {
        foreach(var handle in widgetOwnedWindows.Keys)
            if(IsWindowVisible(handle)&&GetWindowRect(handle,out var r)&&r.Right>r.Left&&r.Bottom>r.Top)
                yield return new(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top);
    }
    void DisposeWidgetBoard()
    {
        widgetTimer.Stop();pageSaveTimer.Stop();CloseWidgetConfiguration();
        foreach(var app in widgetOwnedWindows.Values)app.Changed-=OnWidgetOwnedChanged;
        widgetOwnedWindows.Clear();widgetScheduler.Updated-=OnWidgetUpdated;widgetScheduler.Dispose();
        scene.DetailsPageRequested-=SelectDetailsPage;scene.BoardViewportChanged-=OnBoardViewportChanged;
        widgetTimer.Tick-=OnWidgetTimer;pageSaveTimer.Tick-=OnPageSaveTimer;
        widgetStore.Dispose();widgetSaveGate.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] struct WidgetWindowRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd,out WidgetWindowRect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
}
