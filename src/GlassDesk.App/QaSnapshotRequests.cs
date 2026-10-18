using System.Diagnostics;
using System.Text.Json;
using GlassDesk.NativeBridge;
using Microsoft.UI.Dispatching;

namespace GlassDesk.App;

/// <summary>Explicit preview-only QA. No watcher or file polling exists in ordinary runs.</summary>
sealed class QaSnapshotRequests : IDisposable
{
    readonly NativeWidgetHost host;
    readonly string path;
    readonly DispatcherQueueTimer timer;
    double? finishAt;
    bool disposed;
    static double Now=>Stopwatch.GetTimestamp()*1000d/Stopwatch.Frequency;
    public QaSnapshotRequests(NativeWidgetHost host,DispatcherQueue queue,string requestPath)
    {
        this.host=host;path=requestPath;
        timer=queue.CreateTimer();timer.Interval=TimeSpan.FromMilliseconds(200);timer.Tick+=Tick;timer.Start();
    }
    void Tick(DispatcherQueueTimer sender,object args)
    {
        try
        {
            if(File.Exists(path))
            {
                string value=File.ReadAllText(path);File.Delete(path);
                int duration=int.TryParse(value,out var requested)?Math.Clamp(requested,500,5000):5000;
                host.BeginDiagnosticSnapshot(duration);finishAt=Now+duration+500;
                Write("exposed");
            }
            else if(finishAt is { } deadline&&Now>=deadline){finishAt=null;Write("restored");}
        }
        catch(Exception error){finishAt=null;Write("failed",error.ToString());}
    }
    void Write(string phase,string? error=null)
    {
        try{File.WriteAllText(path+".status.json",JsonSerializer.Serialize(new{Phase=phase,At=DateTimeOffset.UtcNow,Status=host.DiagnosticSnapshotStatus,Error=error}));}
        catch(Exception failure) when(failure is IOException or UnauthorizedAccessException){ }
    }
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Tick-=Tick;}
}
