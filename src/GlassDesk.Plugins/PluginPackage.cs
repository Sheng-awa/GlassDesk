using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using GlassDesk.Core;

namespace GlassDesk.Plugins;

public static partial class PluginPackage
{
    internal static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
    [GeneratedRegex("^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$",RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
    public static void Validate(PluginManifest manifest)
    {
        if(string.IsNullOrWhiteSpace(manifest.Id)||string.IsNullOrWhiteSpace(manifest.Name)||string.IsNullOrWhiteSpace(manifest.Version)||string.IsNullOrWhiteSpace(manifest.EntryType)||string.IsNullOrWhiteSpace(manifest.EntryAssembly)||manifest.ResourceDescription is null)
            throw new InvalidDataException("插件清单缺少必填字段。");
        if(manifest.Id.Length>80||!Identifier().IsMatch(manifest.Id))throw new InvalidDataException("插件标识无效。");
        if(manifest.ApiVersion!=PluginApi.Version)throw new InvalidDataException("插件 API 版本不兼容。");
        if(!Version.TryParse(manifest.Version,out _)||manifest.Name.Length is <1 or >120||manifest.EntryType.Length is <1 or >256)
            throw new InvalidDataException("插件清单无效。");
        if(!manifest.EntryAssembly.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("入口必须是 C# 程序集。");
        SafePath(Path.GetTempPath(),manifest.EntryAssembly);
        if(manifest.ResourceDescription.Length>2048)throw new InvalidDataException("资源说明过长。");
        if(manifest.Id.StartsWith("glassdesk.",StringComparison.Ordinal))throw new InvalidDataException("内置插件标识保留。");
    }
    public static string SafePath(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(':')||relative.Contains('\0'))
            throw new InvalidDataException("插件路径无效。");
        var pieces=relative.Replace('\\','/').Split('/');
        if(pieces.Any(p=>p is ".." or "."||p.EndsWith('.')||p.EndsWith(' ')||p.IndexOfAny(['<','>','|','?','*'])>=0))
            throw new InvalidDataException("插件路径越界或名称无效。");
        var result=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        var prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!result.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("插件路径越界。");
        return result;
    }
    public static PluginManifest ReadManifest(string directory)
    {
        string path=Path.Combine(directory,"plugin.json");
        if(new FileInfo(path).Length>32768)throw new InvalidDataException("插件清单过大。");
        var manifest=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path),JsonOptions)??throw new InvalidDataException("缺少插件清单。");
        Validate(manifest);
        if(!File.Exists(SafePath(directory,manifest.EntryAssembly)))throw new InvalidDataException("入口程序集不存在。");
        return manifest;
    }
    public static PluginManifest Extract(string packagePath,string staging)
    {
        if(!packagePath.EndsWith(".glassdesk-plugin",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("请选择 .glassdesk-plugin 本地包。");
        using var archive=ZipFile.OpenRead(packagePath);
        if(archive.Entries.Count>2048)throw new InvalidDataException("插件文件过多。");
        long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(staging);
        foreach(var entry in archive.Entries)
        {
            total=checked(total+entry.Length);
            if(total>128*1024*1024||!names.Add(entry.FullName.Replace('\\','/')))throw new InvalidDataException("插件超过大小限制或存在重复路径。");
            if(((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("插件不允许符号链接。");
            string destination=SafePath(staging,entry.FullName);
            if(entry.FullName.EndsWith('/')||entry.FullName.EndsWith('\\')){Directory.CreateDirectory(destination);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input=entry.Open();using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write);
            input.CopyTo(output);
        }
        return ReadManifest(staging);
    }
}
