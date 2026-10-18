namespace GlassDesk.Core;

/// <summary>JSON dictionary with value equality so preference snapshots compare by content.</summary>
public sealed class PluginEnableMap : Dictionary<string,bool>,IEquatable<PluginEnableMap>
{
    public PluginEnableMap():base(StringComparer.OrdinalIgnoreCase){ }
    public PluginEnableMap(IDictionary<string,bool> values):base(values,StringComparer.OrdinalIgnoreCase){ }
    public bool Equals(PluginEnableMap? other)=>other is not null&&Count==other.Count&&this.All(p=>other.TryGetValue(p.Key,out var value)&&value==p.Value);
    public override bool Equals(object? other)=>other is PluginEnableMap map&&Equals(map);
    public override int GetHashCode()
    {int hash=0;foreach(var pair in this)hash^=HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(pair.Key),pair.Value);return hash;}
}
