using System.IO;
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
    int homePressedAction = -1;
    string? boardDragInstance;
    WidgetInstance? boardUndoHidden;
    int boardUndoHiddenIndex = -1;
    string? boardUndoHiddenLabel;
    float boardHiddenScroll;
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
        var presented=WidgetBoardData.TimeOnlyForPresentation(state);
        // The widget page follows its content; the dock inset is included so cards stay clear.
        // The Home page uses the reviewed 360x512 canvas; the music page follows media.
        float? height=pages.CurrentPageId==DetailsPageController.Home
            ?Math.Clamp(HomeLayout.Height,WidgetScene.MinimumBoardPageHeight,512)
            :pages.CurrentPageId==DetailsPageController.Widgets
            ?Math.Clamp(WidgetBoardLayout.Pack(presented,width).ContentBottom + WidgetBoardLayout.BottomInset,
                WidgetScene.MinimumBoardPageHeight,512):null;
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
            new(0,WidgetBoardLayout.Top+render.ScrollOffset,scene.CurrentSize.X,Math.Max(0,scene.CurrentSize.Y-WidgetBoardLayout.Top-WidgetBoardLayout.BottomInset)));
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
        Render();ScheduleTextTone();WakeMotion();Changed?.Invoke();
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
                case WidgetBoardActionKind.AddTime:
                case WidgetBoardActionKind.AddDate:
                    if(!WidgetBoardData.CanAdd(boardEditor.State))
                    {
                        // Capacity is a full saved-instance limit, so explain it here instead
                        // of letting the editor throw for a control the page just offered.
                        boardAddMenu=false;notice="组件已达上限（32 个），请先移除部分组件。";
                        boardRevision++;Render();Changed?.Invoke();return;
                    }
                    AddBuiltIn(action.Kind==WidgetBoardActionKind.AddTime?BuiltInWidgetIds.Time:BuiltInWidgetIds.Date);break;
                case WidgetBoardActionKind.BeginEdit when WidgetBoardData.Counts(boardEditor.State).Visible>0:
                    boardEditor.Begin();boardAddMenu=false;break;
                case WidgetBoardActionKind.BeginEdit: return;
                case WidgetBoardActionKind.OpenHidden: OpenHiddenPanel();return;
                case WidgetBoardActionKind.CloseHidden: CloseHiddenPanel();return;
                case WidgetBoardActionKind.RemoveHidden when action.InstanceId is { } hiddenId: RemoveHiddenInstance(hiddenId);return;
                case WidgetBoardActionKind.UndoHidden: UndoHiddenRemoval();return;
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
    static string InstanceDisplayName(WidgetInstance instance)=>instance.ProviderId switch
    { BuiltInWidgetIds.Time=>"时间", BuiltInWidgetIds.Date=>"日期", _=>"组件" };
    // Hidden management is an explicit, reversible transaction: removing one saved instance
    // never touches the others, and undo restores it at its original order until the sheet closes.
    void OpenHiddenPanel()
    {
        boardAddMenu=false;boardHiddenScroll=0;boardPressedAction=-1;
        boardUndoHidden=null;boardUndoHiddenIndex=-1;boardUndoHiddenLabel=null;
        scene.SetBoardHiddenState(true,null,0);
        boardRevision++;Render();WakeMotion();
    }
    void CloseHiddenPanel()
    {
        boardUndoHidden=null;boardUndoHiddenIndex=-1;boardUndoHiddenLabel=null;boardHiddenScroll=0;
        scene.SetBoardHiddenState(false,null,0);
        boardRevision++;Render();
    }
    void RemoveHiddenInstance(string instanceId)
    {
        var saved=boardEditor.State;
        int index=saved.Instances.ToList().FindIndex(i=>i.InstanceId==instanceId);
        if(index<0)return;
        var instance=saved.Instances[index];
        if(!boardEditor.RemoveSaved(instanceId))return;
        boardUndoHidden=instance;boardUndoHiddenIndex=index;boardUndoHiddenLabel=InstanceDisplayName(instance);
        QueueWidgetSave(boardEditor.State);
        boardRevision++;ClampHiddenScroll();
        scene.SetBoardHiddenState(true,boardUndoHiddenLabel,boardHiddenScroll);
        Render();WakeMotion();
    }
    void UndoHiddenRemoval()
    {
        if(boardUndoHidden is null)return;
        if(WidgetBoardData.CanAdd(boardEditor.State)&&boardEditor.InsertSaved(boardUndoHidden,boardUndoHiddenIndex))
            QueueWidgetSave(boardEditor.State);
        boardUndoHidden=null;boardUndoHiddenIndex=-1;boardUndoHiddenLabel=null;
        boardRevision++;ClampHiddenScroll();
        scene.SetBoardHiddenState(true,null,boardHiddenScroll);
        Render();
    }
    void ClampHiddenScroll()
    {
        int hiddenCount=WidgetBoardData.Counts(boardEditor.State).Hidden;
        const float panelTopOffset=WidgetBoardLayout.Top-6, titleHeight=36, rowHeight=48, undoHeight=44;
        float available=Math.Max(0,scene.CurrentSize.Y-panelTopOffset-WidgetBoardLayout.BottomInset);
        float panelHeight=Math.Min(titleHeight+hiddenCount*rowHeight+undoHeight+12,available);
        float rowsHeight=Math.Max(0,panelHeight-titleHeight-undoHeight-8);
        float maximum=Math.Max(0,hiddenCount*rowHeight-rowsHeight);
        boardHiddenScroll=Math.Clamp(boardHiddenScroll,0,maximum);
    }
    void ScrollHiddenPanel(float delta)
    {
        boardHiddenScroll=Math.Max(0,boardHiddenScroll+delta);
        ClampHiddenScroll();
        scene.SetBoardHiddenState(true,boardUndoHiddenLabel,boardHiddenScroll);
        Render();
    }
    void CommitBoardOnLeave()
    {
        if(widgetStore is null)return;
        CloseWidgetConfiguration();boardAddMenu=false;boardPressedAction=-1;boardDragInstance=null;
        boardUndoHidden=null;boardUndoHiddenIndex=-1;boardUndoHiddenLabel=null;boardHiddenScroll=0;
        scene.SetBoardInteractionFeedback(-1,null);scene.SetBoardHiddenState(false,null,0);
        if(boardEditor.IsEditing){QueueWidgetSave(boardEditor.Commit());boardRevision++;}
        if(scene.CaptureOwner==PointerCaptureOwner.WidgetBoard)scene.SetCapture(PointerCaptureOwner.None,Now);
        if(pages.SavePending&&pageSaveTimer is not null){pageSaveTimer.Stop();if(!quitting)pageSaveTimer.Start();}
    }
    void QueueWidgetSave(WidgetBoardState state) => widgetSaveTail=SaveWidgetAfterAsync(widgetSaveTail,state);
    async Task SaveWidgetAfterAsync(Task preceding,WidgetBoardState state)
    {
        await preceding;
        await widgetSaveGate.WaitAsync();
        string? priorSaveNotice=pageSaveNotice;
        try
        {
            await widgetStore.SaveAsync(state);
            pageSaveNotice=null;
            if(notice==priorSaveNotice)notice=null;
            Render();
        }
        catch(Exception error){if(!disposed){
            diagnostics.RecordError("widget-save",error);
            pageSaveNotice="组件配置未保存，请检查数据目录后重试。";
            Render();Changed?.Invoke();}}
        finally {widgetSaveGate.Release();}
    }

    bool RouteBoardPointer(HostPointerEvent e,int id)
    {
        if(controller.Form!=WidgetForm.Details||pages.CurrentPageId!=DetailsPageController.Widgets)return false;
        var actions=scene.BoardRender?.Actions;
        var control=scene.BoardRender?.Controls.FirstOrDefault(c=>c.Id==id);
        bool known=actions?.ContainsKey(id)==true;
        bool target=known&&control is {Enabled:true};
        if(e.Kind==HostEventKind.Invoke)
        {
            if(target)ExecuteBoardAction(actions![id]);
            return known; // disabled controls consume their automation invoke, never execute.
        }
        if(e.Kind==HostEventKind.Down)
        {
            if(!known)return boardPressedAction>=0;
            if(!target)return true; // disabled: consume the press, no side effect.
            boardPressedAction=id;
            scene.SetBoardInteractionFeedback(id,null);Render();
            if(actions![id] is {Kind:WidgetBoardActionKind.DragHandle,InstanceId:{ } instance} && boardEditor.IsEditing)
            {
                boardDragInstance=instance;boardEditor.PointerDown(instance,new(e.X,e.Y));
                scene.SetCapture(PointerCaptureOwner.WidgetBoard,Now);
            }
            return true;
        }
        if(boardPressedAction<0)
        {
            // A disabled board control still owns its click: it was consumed on Down and
            // must not bubble into the body/drag routes on release.
            return known;
        }
        if(e.Kind==HostEventKind.Move&&boardDragInstance is not null)
        {
            if(boardEditor.PointerMove(new(e.X,e.Y)))scene.SetCapture(PointerCaptureOwner.WidgetBoard,Now);
            scene.SetBoardInteractionFeedback(boardPressedAction,DropInstanceId(BoardDropIndex(e.X,e.Y)));
            Render();
            UpdateOutsideGeometry();return true;
        }
        if(e.Kind==HostEventKind.Up)
        {
            int pressed=boardPressedAction;boardPressedAction=-1;
            if(boardDragInstance is not null)
            {
                int? index=BoardDropIndex(e.X,e.Y);
                var outcome=boardEditor.PointerUp(new(e.X,e.Y),new(0,WidgetBoardLayout.Top,scene.CurrentSize.X,Math.Max(0,scene.CurrentSize.Y-WidgetBoardLayout.Top-WidgetBoardLayout.BottomInset)),index);
                boardDragInstance=null;scene.SetCapture(PointerCaptureOwner.None,Now);
                scene.SetBoardInteractionFeedback(-1,null);UpdateOutsideGeometry();
                if(outcome!=WidgetDragRelease.NoDrag){boardRevision++;Render();WakeMotion();}
                else Render();
            }
            else
            {
                scene.SetBoardInteractionFeedback(-1,null);
                if(pressed==id&&actions?.TryGetValue(id,out var action)==true)ExecuteBoardAction(action);
                else Render();
            }
            return true;
        }
        if(e.Kind==HostEventKind.CaptureLost)
        {
            boardEditor.CancelDrag();boardDragInstance=null;boardPressedAction=-1;
            scene.SetBoardInteractionFeedback(-1,null);scene.SetCapture(PointerCaptureOwner.None,Now);
            UpdateOutsideGeometry();Render();return true;
        }
        return true;
    }
    bool RouteHomePointer(HostPointerEvent e,int id)
    {
        if(!scene.HomePage||controller.Form!=WidgetForm.Details)return false;
        var actions=scene.HomeActions;
        bool known=actions.ContainsKey(id);
        if(e.Kind==HostEventKind.Invoke)
        {
            if(known && scene.GetAccessibleControls().FirstOrDefault(c=>c.Id==id) is not {Enabled:false})
                ExecuteHomeAction(actions[id]);
            return known;
        }
        if(e.Kind==HostEventKind.Down)
        {
            if(!known)
            {
                if(scene.HomePopoverOpen){scene.CloseHomePopover();Render();return true;}
                homePressedAction=-1;return false;
            }
            if(scene.HomePopoverOpen && !IsHomePopoverAction(id))scene.CloseHomePopover();
            homePressedAction=id;scene.SetHomePointerFeedback(id);Render();
            return true;
        }
        if(homePressedAction<0)return known;
        if(e.Kind==HostEventKind.Move){scene.SetHomePointerFeedback(homePressedAction);return true;}
        if(e.Kind==HostEventKind.Up)
        {
            int pressed=homePressedAction;homePressedAction=-1;scene.SetHomePointerFeedback(-1);
            if(pressed==id&&actions.TryGetValue(id,out var action))ExecuteHomeAction(action);
            else Render();
            return true;
        }
        if(e.Kind==HostEventKind.CaptureLost)
        {
            homePressedAction=-1;scene.SetHomePointerFeedback(-1);Render();return true;
        }
        return known;
    }
    static bool IsHomePopoverAction(int id)=>
        id==WidgetActions.HomeClosePopover||id==WidgetActions.HomeOpenFolder||
        (id>=WidgetActions.HomePreviewFirst&&id<WidgetActions.HomePreviewFirst+WidgetActions.HomePreviewCount);
    void ExecuteHomeAction(HomeAction action)
    {
        notice=null;
        switch(action.Kind)
        {
            case HomeActionKind.App: LaunchHomeApp(action.Index);return;
            case HomeActionKind.ToggleNav: scene.SetHomeNavExpanded(!scene.HomeNavExpanded);break;
            case HomeActionKind.NavEntry:
                if(action.Index==0)SelectDetailsPage(DetailsPageController.Music);
                else if(action.Index==1)SelectDetailsPage(DetailsPageController.Widgets);
                else ShowSettings();
                return;
            case HomeActionKind.Folder: ShowHomeFolder(action.Index);return;
            case HomeActionKind.Preview:
            {
                var entry=scene.HomeFolderPreview?.Entries.ElementAtOrDefault(action.Index);
                if(entry is not HomeFolderEntry value)return;
                if(system.OpenExplorer(value.Path,!value.IsFolder,out var error))scene.CloseHomePopover();
                else notice=error;
                break;
            }
            case HomeActionKind.OpenFolder:
            {
                var folder=scene.HomeFolderPreview;
                if(folder is null)return;
                if(system.OpenExplorer(folder.Path,false,out var error))scene.CloseHomePopover();
                else notice=error;
                break;
            }
            case HomeActionKind.ClosePopover: scene.CloseHomePopover();break;
        }
        Render();WakeMotion();Changed?.Invoke();
    }
    void ShowHomeFolder(int index)
    {
        string path=HomeFolderPath(index);
        var entries=new List<HomeFolderEntry>();
        try
        {
            foreach(var item in Directory.EnumerateFileSystemEntries(path).Take(3))
                entries.Add(new(Path.GetFileName(item),item,Directory.Exists(item)));
        }
        catch(Exception error){diagnostics.RecordError("home.folder",error);notice="无法读取文件夹。";}
        scene.OpenHomePopover(index,new HomeFolderPreview(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)),path,entries));
        Render();Changed?.Invoke();
    }
    static string HomeFolderPath(int index)=>index switch
    {
        0=>Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        1=>Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        _=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),
    };
    void LaunchHomeApp(int index)
    {
        switch(index)
        {
            case 0: OpenMusic();return;
            case 1: LaunchExternal("msedge");return;
            case 2: LaunchExternal("wt");return;
            case 3:
                if(system.OpenExplorer(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),false,out var error))notice=null;else notice=error;
                break;
            case 4: LaunchExternal("notepad");return;
            default: LaunchExternal("calc");return;
        }
        Render();Changed?.Invoke();
    }
    void LaunchExternal(string target)
    {
        if(system.LaunchExternal(target,out var error))notice=null;else notice=error;
        Render();Changed?.Invoke();
    }
    string? DropInstanceId(int? index)
    {
        if(index is not int slot||slot<0||slot>=boardEditor.State.Instances.Count)return null;
        return boardEditor.State.Instances[slot].InstanceId;
    }
    int? BoardDropIndex(float x,float y)
    {
        var cards=scene.BoardRender?.Packing.Cards;
        if(cards is null||cards.Count==0)return null;
        Vector2 p=new(x,y+scene.CurrentPageScroll);
        var target = cards.OrderBy(c => Vector2.DistanceSquared(p,
            new(c.Bounds.X + c.Bounds.Width / 2, c.Bounds.Y + c.Bounds.Height / 2))).First();
        // Presentation may hide saved date/plugin instances. Reorder against the
        // complete saved board, not an index from the filtered visible cards.
        var instances = boardEditor.State.Instances;
        for (int index = 0; index < instances.Count; index++)
            if (instances[index].InstanceId == target.InstanceId) return index;
        return null;
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


