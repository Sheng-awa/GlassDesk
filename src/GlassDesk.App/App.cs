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
            coordinator?.Quit();
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
        try
        {
            string directory=options.ErrorDirectory;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"startup-error.txt"),$"{DateTimeOffset.UtcNow:O}\n{error.GetType().FullName}\nHRESULT=0x{error.HResult:X8}\n{error.StackTrace}");
        }
        catch(IOException) { }
    }
}
