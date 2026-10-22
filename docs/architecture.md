# GlassDesk 架构

当前为 Windows 桌面应用：WinUI 3 设置窗口，加上 Windows.UI.Composition 自绘桌面组件。官网 HTML 是独立宣传演示，与真实应用渲染分开。

| 模块 | 职责 |
| --- | --- |
| [Core](../src/GlassDesk.Core) | 状态、偏好、布局和插件共享契约 |
| [App](../src/GlassDesk.App) | WinUI 设置、组件内容、场景与交互协调 |
| [Platform](../src/GlassDesk.Platform) | 网易云媒体、进程音频、偏好存储、诊断与 Windows 集成 |
| [Plugins](../src/GlassDesk.Plugins) | 可信本地包、生命周期、配置与贡献管理 |
| [NativeBridge](../src/GlassDesk.NativeBridge) | 托管 / 原生 ABI 与资源所有权封装 |
| [Native](../native/GlassDesk.Native) | HWND、Composition、DirectWrite、图形与捕获、进程 loopback、UIA |

设置与桌面组件使用不同 Composition 对象体系，不能直接互传。App 决定布局与业务状态；原生层提供通用窗口、渲染、捕获与输入互操作。

媒体链路为网易云系统媒体会话 → 媒体快照 → Core / App → 场景呈现。音频可视化与显示器背景采样分别受启停条件约束；边界见 [隐私说明](privacy.md)。第三方插件在主进程运行，不是安全沙箱。

偏好默认存放在用户数据目录，带独立的结构版本与原子替换。关闭设置不会退出驻留应用；应用协调器负责退出与资源释放。

开发入口见 [构建说明](build.md)、[插件开发](plugin-development.md)；具体签名以当前源码为准。早期接口讨论与内部测试记录不作为当前公开版本验收依据。
