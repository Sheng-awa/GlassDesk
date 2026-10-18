# GlassDesk 0.2.0 本地插件开发

插件为可信 C# 进程内扩展，目标 `net10.0`，API 版本为 1。每个外部插件使用可回收 AssemblyLoadContext；`GlassDesk.Core` 契约、框架与 WinUI 程序集由宿主共享。它不是安全沙箱，无法隔离原生崩溃。没有在线市场、后台下载或上传。

参考 `samples/StatusPlugin`：注册功能内容页、两个命令、文字配置和有限区域的文本绘制；计数写入自己的配置，异常命令用来验证宿主移除贡献并保留音乐功能。示例不联网、不启动后台任务。

## 接口与生命周期

实现 `IGlassDeskPlugin.Initialize`、`RegisterContributions`、`StartAsync`、`PauseAsync`、`StopAsync` 和 `DisposeAsync`。初始化保存上下文；注册返回内容页、命令、设置、角色或材质。运行与暂停可以重复，隐藏会暂停但不改变 Enabled。暂停应停止计时器、采集和后台工作；停止与释放必须取消任务、撤销事件、释放资源。

`IPluginContext.Scene` 与 `SceneChanged` 提供真实媒体、当前形态、可见性、减少动画和单调时间戳。`SaveConfigurationAsync` 保存最多 64 KiB UTF-8 JSON；配置位于 `plugin-config/<id>.json`，与二进制分离，同一标识更新及移除默认保留配置。设置贡献由宿主编辑并写入该 JSON。

UI 请求通过宿主队列处理；隐藏或停止后旧队列请求被撤销。`SubmitVisual` 必须对应已注册内容页，每次最多 128 个有限坐标绘制操作；宿主负责布局、输入与窗口。`SubmitCompanion` 和 `SetMaterial` 同样需要对应注册贡献。不要自行抢焦点或接管主组件窗口。

启动等待上限 5 秒、命令上限 10 秒。可捕获异常停止该插件贡献，显示原因；无法停止的任务或无法回收的上下文阻止热更新／移除并要求重启。原生依赖在清单中设置 `requiresRestart: true`。

避免把插件定义的类型放入进程级缓存，例如默认 JsonSerializer 的类型元数据缓存可能保持程序集存活。示例使用共享框架类型的字典序列化；移除时宿主会实测上下文是否释放，不能仅凭调用 Unload 承诺热卸载成功。

## 清单与打包

包为 ZIP，扩展名 `.glassdesk-plugin`，根目录必须包含 `plugin.json`：

```json
{
  "id": "example.local-status",
  "name": "本地状态示例",
  "version": "1.0.0",
  "apiVersion": 1,
  "entryAssembly": "StatusPlugin.dll",
  "entryType": "GlassDesk.Sample.StatusPlugin",
  "resourceDescription": "按操作刷新，无后台任务、采集或联网。",
  "requiresRestart": false
}
```

标识只用小写字母、数字、点与短横线，`glassdesk.*` 保留给内置插件。包内加入入口程序集、私有托管依赖及资源；不要打包宿主契约或整个 .NET 运行时。项目启用 `EnableDynamicLoading`，Core 引用设置 `Private=false`。资源通过项目的 `CopyToPublishDirectory` 输出到包内。

在 GlassDesk 根目录执行：

```powershell
.\scripts\package-plugin.ps1
# 自定义项目、清单和输出目录（输出须在本项目内）
.\scripts\package-plugin.ps1 -Project 'samples\MyPlugin\MyPlugin.csproj' -Manifest 'samples\MyPlugin\plugin.json'
```

脚本锁定还原、发布托管插件、添加清单、创建本地包并生成 SHA-256。输出在 `artifacts/plugins`。宿主检查 API、入口、重复标识与路径；拒绝越界、绝对路径、符号链接、重复文件以及超过 128 MiB／2,048 项的包。

设置“插件”页导入本地包后独立启用；同一标识包替换旧版本并保留配置。详情页工具入口显示功能内容与操作。开发检查：

```powershell
dotnet build tests\GlassDesk.PluginChecks -c Release -m:1 -p:BuildInParallel=false
dotnet run --project tests\GlassDesk.PluginChecks -c Release --no-build
```

资源说明必须描述实际任务与已测成本，不承诺未经测量的 CPU/GPU 数字。参见 `docs/audio-0.2.0.md` 和 `docs/native-material-0.2.0.md`。
