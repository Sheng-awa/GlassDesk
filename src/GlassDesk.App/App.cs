using Microsoft.UI.Xaml;

namespace GlassDesk.App;

public sealed partial class ResidentApplication : Application
{
    readonly LaunchOptions options;
    GlassDeskCoordinator? coordinator;
    internal ResidentApplication(LaunchOptions options)
    {
        this.options=options;
        TraceStartup("application-constructor");
        try { InitializeComponent(); }
        catch(Exception error) { WriteStartupFailure(error); throw; }
        TraceStartup("application-resources-ready");
        UnhandledException+=(_,e)=>
        {
            e.Handled=true;
            WriteStartupFailure(e.Exception);
            if(coordinator is not null)coordinator.Quit();else Exit();
        };
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        TraceStartup("application-launched");
        try {coordinator=new(options,Exit);coordinator.Start();}
        catch(Exception error) {WriteStartupFailure(error);coordinator?.Dispose();Exit();}
    }
    void TraceStartup(string stage)
    {
        if(options.QaReport is null)return;
        try
        {
            Directory.CreateDirectory(options.ErrorDirectory);
            File.AppendAllText(Path.Combine(options.ErrorDirectory,"qa-bootstrap.log"),$"{DateTimeOffset.Now:O} {stage}\n");
        }
        catch(Exception failure) when(failure is IOException or UnauthorizedAccessException){ }
    }
    void WriteStartupFailure(Exception error)
    {
        string? path=FailureReporter.Write(options.ErrorDirectory,"startup-error.txt",error,options.UiPreview);
        if(options.QaReport is null&&!options.QuitExisting)FailureReporter.NotifyOnce("GlassDesk 发生错误，无法继续运行。请保留诊断文件以便排查。",path);
    }
}
