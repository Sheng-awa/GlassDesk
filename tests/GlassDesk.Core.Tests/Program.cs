using GlassDesk.Core;
using System.Diagnostics;

int checks=0;
void Check(bool result,string name) { if(!result){Console.Error.WriteLine("FAIL "+name);Environment.Exit(1);}checks++;Console.WriteLine("PASS "+name); }
MonitorDescriptor Monitor(float scale=1)=>new("test",new(0,0,1920,1080),new(0,0,1920,1040),scale,true);
WidgetController Controller()=>new(new(),Monitor(),new(960,24));
HostPointerEvent Point(HostEventKind kind,int x=960,int y=24)=>new(kind,0,0,x,y);

var c=Controller();
c.HandlePointer(Point(HostEventKind.Enter),0,0);c.Tick(349);
Check(c.Form==WidgetForm.Capsule,"hover stays capsule before 350 ms");
c.HandlePointer(Point(HostEventKind.Leave),0,349);c.Tick(1000);
Check(c.Form==WidgetForm.Capsule,"quick crossing cancels delayed expansion");
c.HandlePointer(Point(HostEventKind.Enter),0,1100);c.Tick(1450);
Check(c.Form==WidgetForm.Peek,"sustained hover expands once");
c.HandlePointer(Point(HostEventKind.Leave),0,1500);c.Tick(1949);
Check(c.Form==WidgetForm.Peek,"leave delay keeps peek until threshold");
c.HandlePointer(Point(HostEventKind.Enter),0,1949);c.Tick(2200);
Check(c.Form==WidgetForm.Peek,"return cancels pending collapse");
int requested=0;c.ActionRequested+=id=>requested=id;
c.HandlePointer(Point(HostEventKind.Down),WidgetActions.Toggle,2210);c.HandlePointer(Point(HostEventKind.Up),WidgetActions.Toggle,2220);
Check(requested==WidgetActions.Toggle && c.Form==WidgetForm.Peek,"play action does not bubble to details");
c.HandlePointer(Point(HostEventKind.Down),0,2230);c.HandlePointer(Point(HostEventKind.Up),0,2240);
c.HandlePointer(Point(HostEventKind.Leave),0,2241);c.Tick(4000);
Check(c.Form==WidgetForm.Details,"details persists after pointer leaves");
c.HandlePointer(Point(HostEventKind.Invoke),WidgetActions.Close,4001);
Check(c.Form==WidgetForm.Capsule,"explicit close returns to capsule");

c=Controller();c.HandlePointer(Point(HostEventKind.Down,960,24),0,0);c.HandlePointer(Point(HostEventKind.Move,965,24),0,10);
Check(!c.IsDragging,"small press motion remains a click");
c.HandlePointer(Point(HostEventKind.Move,1000,55),0,20);
Check(c.IsDragging && c.Anchor==new WidgetAnchor(1000,55),"drag preserves complete down-to-current displacement");
c.HandlePointer(Point(HostEventKind.Up,1000,55),0,30);
Check(c.Form==WidgetForm.Capsule && !c.IsDragging,"drag release cannot become body click");
c=Controller();c.HandlePointer(Point(HostEventKind.Down,960,24),0,0);
c.HandlePointer(Point(HostEventKind.Move,1000,55),0,10);
c.HandlePointer(Point(HostEventKind.Up,1160,164),0,20);
Check(c.Anchor==new WidgetAnchor(1160,164) && !c.IsDragging && c.Form==WidgetForm.Capsule,
    "release point completes displacement when the last move is missing");
c=Controller();c.HandlePointer(Point(HostEventKind.Down,960,24),0,0);
c.HandlePointer(Point(HostEventKind.Up,1160,164),0,20);
Check(c.Anchor==new WidgetAnchor(1160,164) && c.Form==WidgetForm.Capsule,
    "release displacement above the drag threshold cannot become a body click");
c=Controller();c.ChangeForm(WidgetForm.Details,0);c.Tick(300);
c.HandlePointer(Point(HostEventKind.Down,950,600),WidgetActions.Seek,310);c.HandlePointer(Point(HostEventKind.Move,1400,700),WidgetActions.Seek,320);
Check(!c.IsDragging && c.Anchor==new WidgetAnchor(960,24),"seek drag does not move component");

c=Controller();c.ChangeForm(WidgetForm.Details,0);c.ApplySystemScene(new(100,true,false),100);
Check(!c.Visible,"fullscreen hides the component");
c.ToggleVisibility(110);Check(c.Visible && c.Form==WidgetForm.Capsule,"fullscreen hotkey reveals capsule only");
c.ToggleVisibility(120);Check(!c.Visible,"second fullscreen hotkey removes override");
c.ApplySystemScene(new(200,false,false),130);Check(c.Visible && c.Form==WidgetForm.Capsule,"fullscreen exit restores normal rules");
c.ToggleVisibility(140);c.ApplySystemScene(new(100,true,false),150);c.ApplySystemScene(new(200,false,false),160);
Check(!c.Visible,"manual hidden state survives fullscreen round trip");
c.ToggleVisibility(170);c.ChangeForm(WidgetForm.Details,180);c.ApplySystemScene(new(200,false,true),190);
Check(c.Form==WidgetForm.Details,"same maximized foreground does not repeatedly collapse");
c.ApplySystemScene(new(300,false,true),200);Check(c.Form==WidgetForm.Capsule,"new maximized application collapses form");

var motion=new MotionController(new(0,0,232,48,24));
motion.Retarget(new(0,0,360,132,24),0,220);var halfway=motion.Sample(85);
motion.Retarget(new(0,0,232,48,24),85,220);var after=motion.Sample(85);
Check(halfway==after,"interrupted motion starts at actual current pose");
motion.Sample(400);Check(!motion.Active && motion.Current.Width==232,"retarget settles at final requested pose");
motion.Retarget(new(20,30,360,464,24),500,280,true);
Check(!motion.Active && motion.Current.Height==464,"reduced motion keeps state change without animation");
var reducedTarget=new WidgetPose(20,30,360,464,24);
motion.Jump(new(0,0,232,48,24));
motion.Retarget(reducedTarget,600,280);motion.Sample(650);
motion.Retarget(reducedTarget,650,280,true);
Check(!motion.Active && motion.Current==reducedTarget,"enabling reduced motion settles an already active identical target immediately");
if (!args.Contains("--no-stress"))
{
bool stable=true;
for(int i=0;i<1000;i++) {motion.Retarget(new(20,30,i%2==0?360:232,i%2==0?132:48,24),i*11,220);var pose=motion.Sample(i*11+7);stable &= float.IsFinite(pose.Width)&&pose.Width>0;}
Check(stable,"1,000 rapid retargets stay finite and preserve valid sizes");
motion.Sample(12000);Check(!motion.Active,"interrupt stress has no lingering animation");
}
c=Controller();c.RestoreAnchor(new(4000,3000),0);Check(c.Motion.Current.X>=0 && c.Motion.Current.X+c.Layout.Width<=1920 && c.Motion.Current.Y+c.Layout.Height<=1040,"invalid saved position clamps inside work area");
c.SetMonitor(new("secondary",new(-1280,0,1280,720),new(-1280,0,1280,680),1.5f,false),0,true);
Check(c.Motion.Current.X>=-1280 && c.Motion.Current.X+c.Layout.Width*1.5<=0,"monitor removal migrates normalized anchor");

var timestamp=Stopwatch.GetTimestamp();
var media=new MediaSnapshot("test",1,true,"","","",null,PlaybackState.Playing,1,new(TimeSpan.Zero,TimeSpan.FromSeconds(100),TimeSpan.FromSeconds(25),TimeSpan.FromSeconds(5),TimeSpan.FromSeconds(95),timestamp),new(true,true,true,true));
Check(Math.Abs((TimelineProjection.Position(media,timestamp+Stopwatch.Frequency*2)!.Value-TimeSpan.FromSeconds(27)).TotalSeconds)<.01,"playing timeline uses monotonic elapsed time");
Check(TimelineProjection.Position(media with {State=PlaybackState.Paused},timestamp+Stopwatch.Frequency*10)==TimeSpan.FromSeconds(25),"paused progress remains stationary");
Check(TimelineProjection.ClampSeek(media,TimeSpan.FromSeconds(200))==TimeSpan.FromSeconds(95),"seek respects provider range");
Check(TimelineProjection.Position(media with {Timeline=null},timestamp)==null && TimelineProjection.ClampSeek(media with {Capabilities=new()},TimeSpan.FromSeconds(3))==null,"missing timeline or capability cannot synthesize progress");
c=Controller();c.ToggleVisibility(0);c.ApplySystemScene(new(100,true,false),1);c.ToggleVisibility(2);
Check(c.Visible,"fullscreen summon can temporarily reveal manually hidden widget");
c.ToggleVisibility(3);Check(!c.Visible,"second fullscreen toggle only removes temporary summon");
c.ToggleVisibility(4);c.ApplySystemScene(new(200,false,false),5);
Check(!c.Visible,"fullscreen exit after summon preserves prior manual hide");
c.ToggleVisibility(6);Check(c.Visible,"ordinary toggle restores manual visibility independently");
var policy=new CapsuleContentPolicy();var paused=media with {State=PlaybackState.Paused};
policy.Update(paused,100);policy.Tick(5099);
Check(policy.Mode==CapsuleContentMode.Music,"pause 4,999ms keeps capsule music");
policy.Update(paused with {ContentVersion=2,Cover=[1],Title="new metadata"},2000);policy.Tick(5100);
Check(policy.Mode==CapsuleContentMode.Clock,"pause 5,000ms changes capsule without reset by metadata");
Check(policy.ForForm(WidgetForm.Peek)==CapsuleContentMode.Music&&policy.ForForm(WidgetForm.Details)==CapsuleContentMode.Music,"paused expanded forms retain real music");
policy.Update(paused with {SessionToken="replacement"},6000);policy.Tick(10999);
Check(policy.Mode==CapsuleContentMode.Music,"replacement paused session starts its own interval");
policy.Update(media with {SessionToken="replacement"},11000);policy.Tick(16000);
Check(policy.Mode==CapsuleContentMode.Music&&!policy.NeedsTick,"resume cancels pending pause interval");
policy.Update(paused,20000);policy.Tick(30000);
Check(policy.Mode==CapsuleContentMode.Clock,"hidden time counts without restarting pause interval");
policy.Update(MediaSnapshot.Disconnected,30001);
Check(policy.ForForm(WidgetForm.Details)==CapsuleContentMode.Clock,"disconnected expanded form uses clock");
var expected=new[]{(WidgetForm.Capsule,CapsuleContentMode.Clock,200f,52f),(WidgetForm.Peek,CapsuleContentMode.Clock,240f,144f),(WidgetForm.Details,CapsuleContentMode.Clock,360f,300f),(WidgetForm.Capsule,CapsuleContentMode.Music,256f,52f),(WidgetForm.Peek,CapsuleContentMode.Music,320f,176f),(WidgetForm.Details,CapsuleContentMode.Music,360f,476f)};
Check(expected.All(x=>{var metrics=WidgetLayoutMetrics.Resolve(x.Item1,x.Item2);return metrics.Width==x.Item3&&metrics.Height==x.Item4;}),"all six forms use frozen layout metrics");
c=Controller();var center=c.Anchor.CenterX;c.UpdateContent(CapsuleContentMode.Music,true,true,296,0);c.ChangeForm(WidgetForm.Details,10);c.Tick(1000);
Check(Math.Abs(c.Motion.Current.X+c.Motion.Current.Width/2-center)<.01&&c.Motion.Current.Y==24,"content expansion preserves horizontal center and top anchor");
Check(new WidgetPreferences{Version=1,AnchorX=.3,CompanionColor=0x123456}.Validated() is {Version:4,AnchorX:.3,CompanionEnabled:true,LiquidGlassEnabled:false,CompanionColor:0xff123456},"version4 migration preserves placement and builtin defaults");


UiNavigationChecks.Run(Check);
DetailsChromeChecks.Run(Check);
UiCompanionChecks.Run(Check);
CompanionLiquidChecks.Run(Check);
TypographyPageChecks.Run(Check);
WidgetBoardChecks.Run(Check);
OutsidePointerChecks.Run(Check);
ClockStructuredChecks.Run(Check);
HiddenWidgetChecks.Run(Check);
CompactLayoutChecks.Run(Check);

Console.WriteLine($"{checks} checks passed.");
