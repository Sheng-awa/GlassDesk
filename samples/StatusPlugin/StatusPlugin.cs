using GlassDesk.Core;
using System.Text.Json;

namespace GlassDesk.Sample;

/// <summary>A functional local page/command/config/render example with no network activity.</summary>
public sealed class StatusPlugin : IGlassDeskPlugin
{
    IPluginContext? context;
    public void Initialize(IPluginContext value)=>context=value;
    public IEnumerable<IPluginContribution> RegisterContributions()=>
    [
        new PluginContentPage("status","本地状态",[new("刷新计数","计数保存在独立插件配置中","increment"),new("异常回退测试","停止此插件的贡献","throw")]),
        new PluginCommand("increment","增加计数",Increment),
        new PluginCommand("throw","异常回退测试",_=>throw new InvalidOperationException("sample failure")),
        new PluginSetting("caption","状态标签",PluginSettingKind.Text,"本地插件","功能示例的文字标签。")
    ];
    async ValueTask Increment(CancellationToken cancellationToken)
    {
        var config=context!.ReadConfiguration();int count=config is JsonElement c&&c.TryGetProperty("count",out var value)?value.GetInt32():0;
        using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string,object>{{"count",count+1},{"caption","本地插件"}}));
        await context.SaveConfigurationAsync(doc.RootElement,cancellationToken);Render();
    }
    void Render()
    {
        var config=context!.ReadConfiguration();int count=config is JsonElement c&&c.TryGetProperty("count",out var value)?value.GetInt32():0;
        context.SubmitVisual("status",[new(PluginDrawKind.Text,16,8,256,24,0xff59636d,$"本地插件 · 刷新 {count} 次",14)]);
        context.RequestRender();
    }
    public ValueTask StartAsync(CancellationToken cancellationToken){context!.SceneChanged+=OnScene;Render();return ValueTask.CompletedTask;}
    void OnScene(PluginSceneState scene)=>Render();
    public ValueTask PauseAsync(CancellationToken cancellationToken){if(context is not null)context.SceneChanged-=OnScene;return ValueTask.CompletedTask;}
    public ValueTask StopAsync(CancellationToken cancellationToken)=>PauseAsync(cancellationToken);
    public async ValueTask DisposeAsync(){await StopAsync(CancellationToken.None);context=null;}
}
