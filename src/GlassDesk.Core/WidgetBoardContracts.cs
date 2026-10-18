using System.Text.Json;

namespace GlassDesk.Core;

public enum WidgetCardSize { Small, Medium }
public enum WidgetElementKind { Text, Symbol, Separator, ProgressValue }
public enum WidgetNumericStyle { None, Tabular, DigitsOnly }
public static class BuiltInWidgetIds
{
    public const string Time="glassdesk.widgets.time", Date="glassdesk.widgets.date";
}
public sealed record WidgetInstance(string InstanceId,string ProviderId,WidgetCardSize Size,int Order,JsonElement Settings);
public sealed record WidgetBoardState(int Version,IReadOnlyList<WidgetInstance> Instances);
public sealed record WidgetDescriptor(IReadOnlyList<WidgetCardSize> Sizes,string? Description=null);
public sealed record WidgetRequest(string InstanceId,WidgetCardSize Size,DateTimeOffset UtcNow,JsonElement Settings);
public sealed record WidgetElement(WidgetElementKind Kind,FloatRect Bounds,string? Text=null,
    TextRole TextRole=TextRole.Body,WidgetNumericStyle NumericStyle=WidgetNumericStyle.None,
    string? SymbolKey=null,string? CommandId=null)
{
    public double? Value {get;init;}
}
public sealed record WidgetSnapshot(IReadOnlyList<WidgetElement> Elements,DateTimeOffset? NextRefreshUtc=null);
public interface IWidgetProvider
{
    ValueTask<WidgetSnapshot> GetSnapshotAsync(WidgetRequest request,CancellationToken cancellationToken);
}
// Additive contribution: old IPluginContext and all old constructors remain intact.
public sealed record PluginWidget(string Id,string Name,WidgetDescriptor Descriptor,IWidgetProvider Provider):IPluginContribution;
