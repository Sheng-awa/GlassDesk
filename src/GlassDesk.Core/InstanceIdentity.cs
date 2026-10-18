using System.Security.Cryptography;
using System.Text;

namespace GlassDesk.Core;

/// <summary>One identity supplies every IPC name. Formal names stay compatible with 0.2.0.</summary>
public sealed record InstanceIdentity
{
    public bool IsPreview { get; }
    public string Namespace { get; }
    public string MutexName { get; }
    public string WindowMarker { get; }
    public string SettingsMessageName { get; }
    public string QuitMessageName { get; }
    private InstanceIdentity(bool preview, string name)
    {
        IsPreview=preview;Namespace=name;MutexName=@"Local\"+name;WindowMarker=name;
        SettingsMessageName=preview?name+".OpenSettings":"GlassDesk.OpenSettings.v1.643A4E18";
        QuitMessageName=preview?name+".Quit":"GlassDesk.Quit.v1.643A4E18";
    }
    public static InstanceIdentity Formal { get; }=new(false,"GlassDesk.Resident.v1.643A4E18");
    public static InstanceIdentity UiPreview(string dataDirectory)
    {
        string path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory)).Replace('/','\\').ToUpperInvariant();
        string hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant()[..16];
        return new(true,"GlassDesk.UiPreview."+hash);
    }
}
