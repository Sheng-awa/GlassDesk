using System.Reflection;
using System.Runtime.Loader;
using GlassDesk.Core;

namespace GlassDesk.Plugins;

internal sealed class PluginLoadContext(string assemblyPath) : AssemblyLoadContext(isCollectible:true)
{
    readonly AssemblyDependencyResolver resolver=new(assemblyPath);
    readonly string root=Path.GetDirectoryName(assemblyPath)!;
    public bool NativeLoaded {get;private set;}
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if(assemblyName.Name==typeof(IGlassDeskPlugin).Assembly.GetName().Name)return typeof(IGlassDeskPlugin).Assembly;
        if(assemblyName.Name is "netstandard" or "mscorlib"||assemblyName.Name?.StartsWith("System.",StringComparison.Ordinal)==true||assemblyName.Name?.StartsWith("WinRT.",StringComparison.Ordinal)==true)
            return Default.Assemblies.FirstOrDefault(a=>a.GetName().Name==assemblyName.Name);
        if(assemblyName.Name?.StartsWith("Microsoft.UI.",StringComparison.Ordinal)==true||assemblyName.Name?.StartsWith("Microsoft.WindowsAppSDK",StringComparison.Ordinal)==true)
            return Default.Assemblies.FirstOrDefault(a=>a.GetName().Name==assemblyName.Name);
        string? path=resolver.ResolveAssemblyToPath(assemblyName);
        if(path is null)return null;
        if(!Path.GetFullPath(path).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new FileLoadException("插件依赖越界。");
        return LoadFromAssemblyPath(path);
    }
    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        NativeLoaded=true;
        string? path=resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null?0:LoadUnmanagedDllFromPath(path);
    }
}
