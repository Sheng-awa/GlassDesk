namespace GlassDesk.Core;

public readonly record struct WidgetPose(float X, float Y, float Width, float Height, float Radius, float Opacity=1)
{
    internal double[] Values => [X,Y,Width,Height,Radius,Opacity];
    internal static WidgetPose From(double[] v)=>new((float)v[0],(float)v[1],Math.Max(1,(float)v[2]),Math.Max(1,(float)v[3]),Math.Max(0,(float)v[4]),Math.Clamp((float)v[5],0,1));
}

/// <summary>Interruptible critically damped motion, evaluated with a monotonic clock.</summary>
public sealed class MotionController(WidgetPose initial)
{
    private WidgetPose start=initial;
    private double[] velocity=new double[6];
    private double started, ends, omega;
    public WidgetPose Target {get;private set;}=initial;
    public WidgetPose Current {get;private set;}=initial;
    public bool Active {get;private set;}

    private (WidgetPose Pose,double[] Velocity) Evaluate(double now)
    {
        if (!Active || now>=ends) return (Target,new double[6]);
        var a=start.Values; var b=Target.Values; var result=new double[6]; var speed=new double[6];
        double t=Math.Max(0,(now-started)/1000), decay=Math.Exp(-omega*t);
        for(int i=0;i<6;i++)
        {
            double offset=a[i]-b[i], c=velocity[i]+omega*offset;
            result[i]=b[i]+(offset+c*t)*decay;
            speed[i]=(velocity[i]-omega*c*t)*decay;
        }
        return (WidgetPose.From(result),speed);
    }
    public WidgetPose Sample(double now)
    {
        Current=Evaluate(now).Pose;
        if(now>=ends) Active=false;
        return Current;
    }
    public void Retarget(WidgetPose target,double now,int durationMs,bool reducedMotion=false)
    {
        if (reducedMotion || durationMs<=0) { Jump(target); return; }
        if (target==Target && Active) return;
        var current=Evaluate(now);
        start=current.Pose; Current=start; velocity=current.Velocity;
        Target=target; started=now; ends=now+durationMs; omega=8.0/(durationMs/1000.0);
        Active=start!=target || velocity.Any(v=>Math.Abs(v)>.001);
    }
    public void Jump(WidgetPose pose)
    {
        start=Current=Target=pose; velocity=new double[6]; Active=false; started=ends=0;
    }
    public void Translate(float dx,float dy,double now)
    {
        var evaluated=Evaluate(now);
        var target=Target with {X=Target.X+dx,Y=Target.Y+dy};
        var pose=evaluated.Pose with {X=evaluated.Pose.X+dx,Y=evaluated.Pose.Y+dy};
        double remaining=Math.Max(0,ends-now);
        if (!Active || remaining<=0) { Jump(target); return; }
        start=Current=pose; velocity=evaluated.Velocity; Target=target; started=now; ends=now+remaining;
    }
}
