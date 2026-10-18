# GlassDesk 视觉验收

这是独立的手动开发工具，使用生产组件的 `WidgetScene`、`NativeWidgetHost` 和 `WidgetController`，检查真实排版、封面裁切、按钮反馈及可打断的三态动画。

所有音乐状态、播放进度和原创几何网格封面都在工具内生成，窗口与音乐标题明确标注测试数据。工具不连接播放器，不调用 SMTC，不读写生产设置、不注册托盘、快捷键或登录启动。无会话案例读取本机真实时间。此工具不进入安装包。

在仓库根目录先构建 Release 原生桥，再构建工具；运行环境与 GlassDesk 相同（Windows 11 x64、.NET 10 SDK、Windows App SDK 2.5.1）：

```powershell
./scripts/build-native.ps1
dotnet restore ./tools/GlassDesk.VisualReview/GlassDesk.VisualReview.csproj --locked-mode
dotnet build ./tools/GlassDesk.VisualReview/GlassDesk.VisualReview.csproj -c Release --no-restore
./tools/GlassDesk.VisualReview/bin/Release/net10.0-windows10.0.26100.0/win-x64/GlassDesk.VisualReview.exe
```

工具拥有自己的 `ReviewResources.xaml`、PRI 与 `ReviewResources.xbf` 资源。资源名与生产应用不同，避免合并资源索引时重名。需要复制到独立目录时请使用 `dotnet publish`，项目会检查并一同导出这些资源。

选择案例后，用“胶囊 / 悬停 / 详情”切换状态；过渡进行时可以再次切换。组件仍使用真实的悬停、点击、拖动与进度条命中检测。播放、上一首、下一首和进度拖动只修改内存测试数据。选择“减少动画”可核对静态状态，系统关闭动画时也会自动减少动画。

案例覆盖长中英文标题和艺人、640×360 横幅封面、360×640 竖幅封面、缺封面、播放与暂停，以及无音乐会话。封面由标准 PNG 编码在内存生成，没有外部素材或网络依赖。

关闭控制面板或点击“退出视觉验收”会销毁组件和计时器。运行错误记录在工具输出目录的 `visual-review.log`。本工具没有压力循环、自动反复切换或驻留压力测试。
