using GlassDesk.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GlassDesk.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        bool qa=args.Any(a=>a.StartsWith("--qa-report=",StringComparison.Ordinal));
        string errorDirectory=LaunchOptions.EarlyErrorDirectory(args);
        bool preview=LaunchOptions.PreviewBuild||args.Contains("--ui-preview",StringComparer.Ordinal);
        void WriteLog(string name,string value)
        {
            string directory=!preview&&name=="qa-bootstrap.log"?AppContext.BaseDirectory:errorDirectory;
            try{Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,name),value);}
            catch(Exception failure) when(failure is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if(!preview)return;
                try{Directory.CreateDirectory(LaunchOptions.PreviewErrorFallback);File.AppendAllText(Path.Combine(LaunchOptions.PreviewErrorFallback,name),value);}
                catch(Exception fallback) when(fallback is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){ }
            }
        }
        void Trace(string stage){if(qa)WriteLog("qa-bootstrap.log",$"{DateTimeOffset.Now:O} {stage}\n");}
        try
        {
            Trace("main-enter");
            var options=LaunchOptions.Parse(args);
            errorDirectory=options.ErrorDirectory;
            Trace("options-parsed");
            using var gate=new SingleInstanceGate(options.Identity);
            Trace(gate.IsPrimary?"primary-instance":"secondary-instance");
            if(options.QuitExisting) { gate.RequestQuit();return; }
            if(!gate.IsPrimary) { gate.RequestExistingSettings();return; }
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Trace("com-wrappers-ready");
            Application.Start(initializationArgs=>
            {
                Trace("xaml-callback");
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _=new ResidentApplication(options);
                Trace("application-created");
            });
            Trace("xaml-exit");
        }
        catch(Exception error)
        {
            WriteLog("bootstrap-error.txt",$"{DateTimeOffset.UtcNow:O}\n{error.GetType().FullName}\nHRESULT=0x{error.HResult:X8}\n{error.StackTrace}\n");
            Trace("bootstrap-error");
        }
    }
}
