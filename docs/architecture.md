# GlassDesk 0.2.0 架构

主体运行于一个 STA / WinUI UI 线程。Core 决定状态和动作目标，App 决定布局，Platform 提供媒体和 Windows 集成，薄原生 DLL 提供窗口与渲染互操作。所有源代码在本工程内原创实现。

| 项目 | 职责与主要入口 |
|---|---|
| GlassDesk.Core | Contracts、CapsuleContentPolicy、WidgetLayoutMetrics、PluginContracts、WidgetController、MotionController、TimelineProjection；无窗口或媒体 COM 依赖 |
| GlassDesk.App | Program、ResidentApplication、GlassDeskCoordinator、WidgetScene、SettingsWindow；负责资源初始化与统一退出 |
| GlassDesk.Platform | NetEaseMediaAdapter、ProcessAudioVisualizationSource、NetEaseProcessResolver、AudioSpectrumAnalyzer、ForegroundObserver、MonitorService、SystemIntegration、PreferenceStore、Diagnostics、SingleInstanceGate |
| GlassDesk.Plugins | 可信本地包、独立 AssemblyLoadContext、生命周期、配置与贡献撤销；内置陪伴球和液态材质插件 |
| GlassDesk.NativeBridge | DLL ABI 的所有权封装；把 Windows.UI.Composition 接口投影给 C# |
| native/GlassDesk.Native | HWND、HostBackdrop、WGC/D3D11 液态材质、进程 WASAPI loopback、D2D/DirectWrite/WIC 表面、圆角区域、独立穿透阴影、UIA |

数据流为 `GSMTC 的 cloudmusic.exe 会话 → MediaSnapshot → WidgetScene / WidgetController → Composition`。只匹配准确的网易云来源；系统当前媒体源改变不会更换控制对象。元数据、封面和控制结果都检查会话与内容版本，避免过期响应覆盖当前内容。媒体对象操作回到 COM 所属线程。封面最大边 768px，缓存最近三个。

小组件使用 Windows.UI.Composition，设置使用 WinUI 3 的 Microsoft.UI.Composition；两套对象不互传。原生层维护真实背景模糊、白色染色、低强度纹理和亮边；C# 在 DIP 空间维护内容。原生绘图表面按实际 DPI 分配像素。标题、按钮、进度区域分别命中，并提供 UIA Invoke / RangeValue。

宿主维护规范 DIP 尺寸。WM_DPICHANGED 使用消息携带的新 DPI；WM_SETTINGCHANGE / WM_DISPLAYCHANGE 通过从未显示的 PMv2 窗口查询当前显示器 DPI，以纠正旧窗口仍保留旧 DPI 的情况。该查询和应用内回归不会改变 Windows 显示设置。

动画计时器仅在运动或悬停倒计时需要时运行。静止音乐胶囊不轮询布局或时间轴。时钟在分钟边界、系统时间／时区变化及重新显示时刷新；仅大卡片的有效播放时间轴约每 250ms 更新。暂停计时采用单调时间，达到 5,000ms 仅将胶囊切为时钟，不改写真实媒体。封面与歌曲切换不会改变展开状态。

真实频谱由原生进程树 loopback 与复用缓冲区 FFT 提供，25Hz 分析、Composition 插值；不可可靠绑定来源时停止采集。插件 Enabled 与 Running／Paused 状态独立；隐藏暂停任务并撤销旧 UI 队列请求，关闭取消任务、事件和贡献。配置使用每插件写入门，插件登记表允许配置线程并发读取。

液态背景从 WGC 显示器纹理裁剪必要区域，GPU 模糊与折射后接入原生 Composition 外壳；主体和阴影验证采集排除，关闭后恢复原属性。隐藏释放采集和纹理，恢复或故障采用有界重试。正常渲染没有 CPU 像素读回；显式 QA 单帧导出与独立呈现探针用于验收。

系统观察使用 WinEvent 与自有 HWND 消息；不注入游戏或播放器。高对比度通过 Win32 参数和 WM_SYSCOLORCHANGE / WM_SETTINGCHANGE 更新，避免桌面应用不支持的 AccessibilitySettings.HighContrastChanged 事件。图形失效先简化材质，原生层重建图形资源并通知场景丢弃旧表面；终端失败有界重试，设置与驻留尽量保留。

配置带版本并原子替换，损坏文件先备份。诊断最多保留 128 条系统/能力/错误记录，不保存歌曲历史、账号或异常消息内容。正常关闭设置仅隐藏窗口。退出先刷新待保存偏好，再释放系统集成、媒体事件、场景、原生宿主与阴影，最后退出 WinUI 消息循环。

ABI、场景和平台的具体签名分别见 native-api.md、app-api.md、platform-api.md；状态规则见 state-spec.md。测试报告将隐藏消息测试、程序驱动切换和真实指针验证分别列出。
