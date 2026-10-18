using GlassDesk.Core;
namespace GlassDesk.App;

internal sealed record LaunchOptions(string? DataDirectory=null,string? QaReport=null,int QaCycles=0,bool NoSettings=false,bool QuitExisting=false,string? QaMaterialFrame=null,bool UiPreview=false,string? QaSnapshotRequest=null)
{
    internal static bool PreviewBuild
    {
        get
        {
#if GLASSDESK_UI_PREVIEW
            return true;
#else
            return false;
#endif
        }
    }
    public InstanceIdentity Identity=>UiPreview?InstanceIdentity.UiPreview(DataDirectory??Path.Combine(AppContext.BaseDirectory,"preview-data")):InstanceIdentity.Formal;
    public string ErrorDirectory=>DataDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GlassDesk");
    public static string PreviewErrorFallback=>Path.Combine(Path.GetTempPath(),"GlassDesk.UiPreview","errors");
    static string AbsoluteDataDirectory(string value)
    {
        string path=Path.GetFullPath(value);
        string root=Path.GetPathRoot(path)??string.Empty;
        if(path[root.Length..].IndexOfAny(Path.GetInvalidFileNameChars().Where(c=>c is not ('/' or '\\')).ToArray())>=0)
            throw new ArgumentException("The data directory contains invalid path characters.",nameof(value));
        return path;
    }
    public static string EarlyErrorDirectory(string[] args)
    {
        bool preview=PreviewBuild||args.Contains("--ui-preview",StringComparer.Ordinal);
        if(!preview)return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GlassDesk");
        try
        {
            string? data=args.LastOrDefault(a=>a.StartsWith("--data-dir=",StringComparison.Ordinal));
            return data is null?Path.Combine(AppContext.BaseDirectory,"preview-data"):AbsoluteDataDirectory(data[11..]);
        }
        catch(ArgumentException){return PreviewErrorFallback;}
        catch(NotSupportedException){return PreviewErrorFallback;}
        catch(System.IO.IOException){return PreviewErrorFallback;}
    }
    public static LaunchOptions Parse(string[] args)
    {
        string? data=null,report=null,material=null,snapshot=null;int cycles=0;bool hidden=false,quit=false,preview=PreviewBuild;
        foreach(string argument in args)
        {
            if(argument.StartsWith("--data-dir=",StringComparison.Ordinal))data=AbsoluteDataDirectory(argument[11..]);
            else if(argument.StartsWith("--qa-report=",StringComparison.Ordinal))report=Path.GetFullPath(argument[12..]);
            else if(argument.StartsWith("--qa-cycles=",StringComparison.Ordinal) && int.TryParse(argument[12..],out int count))cycles=Math.Clamp(count,0,10000);
            else if(argument=="--no-settings")hidden=true;
            else if(argument=="--quit")quit=true;
            else if(argument=="--ui-preview")preview=true;
            else if(argument.StartsWith("--qa-material-frame=",StringComparison.Ordinal))material=Path.GetFullPath(argument[20..]);
            else if(argument.StartsWith("--qa-snapshot-request=",StringComparison.Ordinal))snapshot=Path.GetFullPath(argument[22..]);
        }
        if(preview&&data is null)data=Path.Combine(AppContext.BaseDirectory,"preview-data");
        return new(data,report,cycles,hidden,quit,material,preview,preview?snapshot:null);
    }
}
