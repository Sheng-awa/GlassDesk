# 0.1 → 0.2 配置迁移

主配置路径保持 `%LocalAppData%/GlassDesk/settings.json`。读取接受版本 1 和 2，内存规范化为版本 2，下次真实设置保存或退出刷新时原子替换。安装器保持原 AppId、当前用户目录和独立配置目录；升级不会重置用户偏好，默认卸载保留配置。

| 字段 | 迁移行为 |
|---|---|
| 位置、MonitorId、AnchorX/Y | 保留；显示器失效时按现有规则回退主屏并限制到工作区 |
| Blur、TintOpacity、ReducedMotion | 保留并验证范围 |
| HoverDelayMs、LeaveDelayMs、AvoidFullscreen | 保留 |
| RunAtStartup、HotkeyModifiers/Key | 保留；绑定失败显示原因，不伪造成功 |
| OnboardingSeen | 保留 |
| AudioVisualizationEnabled | 旧配置未含字段时默认 true |
| CompanionEnabled、CompanionColor | 默认 true、原创深色球体 |
| CompanionFollowMouse | 未含字段时默认 true；关闭后立即停止胶囊外鼠标跟随 |
| ClockShowTime、ClockShowDate、ClockShowWeekday | 未含字段时默认只开启时间；三个选择独立保存 |
| LiquidGlassEnabled | 默认 false |
| PluginEnabled | 每插件独立开关，缺省为空；外部新导入插件默认关闭 |

新版本只接受有限值和正常大小 JSON；不支持的版本、损坏或过大的文件先保留带时间戳备份，再使用默认偏好。插件二进制在 `plugins/<id>`，配置在 `plugin-config/<id>.json`；同标识更新和移除保留该配置。

本机先前流程验证：常规目录安装 0.1.0，通过旧版 UI 保存 Blur=27 和已读引导；运行 0.2.0 安装器后旧进程正常退出，新版 UI 显示 27，保存后 Version=2；卸载保留配置并重新安装成功。旧记录在 `artifacts/qa`，不能据此替代最后发布包的批次核验。

2026-10-15 收尾时再次执行常规目录 0.1→0.2 安装升级和卸载重装，以隔离配置验证位置、模糊、动画、延迟、全屏策略、启动项及快捷键字段保留，新增开关默认值正确，结果为 `artifacts/final-acceptance/migration-result.json`。测试输入 TintOpacity=0.31 低于旧版最小值 0.35，旧版保存时先规范化为 0.35，新版保留了这个有效值；不将它误报成迁移丢失。真实用户配置与测试配置分开，真实用户配置前后 SHA-256 保持 `005943d487758c7cb3305c6b8bc63ef9306d7ef5a03a1c4f8e23c98b9b71a0a8`。最后安装批次及保留核验见 [验收报告](test-report.md)。

## 0.3.0-preview.1 独立预览配置

当前预览接受配置版本1、2、3，内存规范化并保存为版本3。新增`CompanionAppearanceId`区分原生、猫娘与恶魔；未知标识回退原生，球体颜色与形式分别保存。既有时间／日期／星期、鼠标跟随和插件开关继续保留。

预览的默认数据目录是程序旁的`preview-data`，使用配置路径派生的独立实例IPC；不读取或自动迁移正式`%LocalAppData%/GlassDesk`。预览安装器与正式0.2版采用不同AppId和安装目录。预览覆盖安装与卸载保留配置的本机证据见[当前验收记录](ui-acceptance-0.3.0-preview.1.md)；它不代表正式0.2→0.3升级或干净机器迁移已实测。
