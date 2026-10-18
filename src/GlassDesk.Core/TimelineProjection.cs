using System.Diagnostics;
namespace GlassDesk.Core;

public static class TimelineProjection
{
    public static TimeSpan? Position(MediaSnapshot snapshot,long monotonicTimestamp)
    {
        if(snapshot.Timeline is not {Valid:true} timeline) return null;
        double seconds=0;
        if(snapshot.State==PlaybackState.Playing)
            seconds=Math.Max(0,(monotonicTimestamp-timeline.ReceivedTimestamp)/(double)Stopwatch.Frequency)*(snapshot.Rate is >0 ? snapshot.Rate.Value : 1);
        double ticks=Math.Clamp(timeline.Position.Ticks+seconds*TimeSpan.TicksPerSecond,timeline.Start.Ticks,timeline.End.Ticks);
        return TimeSpan.FromTicks((long)ticks);
    }
    public static TimeSpan? ClampSeek(MediaSnapshot snapshot,TimeSpan requested)
    {
        if(!snapshot.Capabilities.Seek || snapshot.Timeline is not {Valid:true} timeline) return null;
        long min=Math.Max(timeline.Start.Ticks,timeline.MinSeek.Ticks),max=Math.Min(timeline.End.Ticks,timeline.MaxSeek.Ticks);
        return max>=min ? TimeSpan.FromTicks(Math.Clamp(requested.Ticks,min,max)) : null;
    }
}
