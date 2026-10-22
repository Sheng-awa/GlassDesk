# GlassDesk 第一版正式发布前：代码与交付审查

日期：2026-10-22。状态：**待用户审查，本轮不修实现**。

后续状态：本文保留修复前快照；用户已授权修运行问题，结果见 [运行问题修复记录](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/docs/runtime-bugfixes-2026-10-22.md>)。正式发布准备与设置重设计未在该轮执行。

**结论：可以开始收敛正式版，但当前还不能直接把试用目录当作正式发布包。** 本次发现一项实际失败的发布检查、原生二进制批次混用隐患、默认小组件覆盖原数据的故障路径，以及详情页透明材质与文字颜色策略的冲突。先处理这些，再做一次针对最终安装包的简短人工确认。

这份报告将“已由命令验证”“已确认的代码路径”“尚待故障注入或人工试用”分开标注。没有把 303 项 Core 检查通过当作整个应用通过，也没有把旧版本的录屏、驻留、安装记录算到本次。

## 1. 审查基线与范围

- 项目：`D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk`。
- 分支：`ui/final`；HEAD：`8891117`，另有未提交实现与新增源文件。审查对象是**当前磁盘内容**，并非仅 HEAD。
- 读取范围：启动与单实例、配置存储与迁移、退出与保存、媒体和频谱、首页文件夹与启动入口、插件装卸、原生桥接、自绘 UI / UIA、正式 / 预览安装与发布脚本。
- 你提供的新图标按当前品牌方向保留。本报告不要求退回黑胶囊，也不修改你的图案；只审查资源接入和实际显示条件。
- 未在本轮执行：运行中的 UI 操作、音频录制 / 播放控制、GPU 性能测试、长时间驻留、混合 DPI 实机、安装 / 升级 / 卸载、原生重新编译、代码签名。
- 本轮只新增本报告、审查证据及隔离测试产物，普通构建会更新 bin/obj。没有修改产品源码、测试源码或发布脚本，没有替换正在使用的独立试用目录。

证据目录：[本次审查证据](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/artifacts/audit/release-readiness-2026-10-22>)。基线文件保存审查时的关键源文件哈希，便于后续区分修复前后。

## 2. 实际执行结果

| 检查 | 结果 | 能证明什么 / 不能证明什么 |
|---|---|---|
| App Release 普通构建，`GlassDeskUiPreview=false`，`--no-restore` | **成功，0 警告、0 错误** | 托管源码可编译；没有重新编译 native，也不证明最终安装包可启动 |
| Core，`--no-stress` | **303 项通过** | 已有逻辑 / 几何检查通过；不能覆盖原生呈现和实际输入 |
| Platform 现有检查 | **退出码 1，失败** | 默认组件数量断言与当前设计不一致；程序在首个失败点退出，后续项目不能记为通过 |
| DLL 导出检查 | 正式默认目录缺少 `GDGetNativeAbiInfo`；preview.2 有该导出 | 只读检查导出，不执行 DLL 初始化；没有声称完整 ABI / 运行时回归通过 |
| 实际 UI / 安装验收 | **未执行** | 留给最终候选包和用户首轮试用 |

日志：[构建](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/artifacts/audit/release-readiness-2026-10-22/managed-build.log>)、[Core](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/artifacts/audit/release-readiness-2026-10-22/core-checks.log>)、[Platform 失败](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/artifacts/audit/release-readiness-2026-10-22/platform-checks.log>)、[原生导出与哈希](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/artifacts/audit/release-readiness-2026-10-22/native-export-inspection.json>)。

## 3. 问题总表

P1：正式发布前优先关闭，或先证明相关风险不存在。P2：高价值修复 / 明确范围，允许你决定是否延后。没有确认到必然影响所有用户的 P0 崩溃。

| ID | 级别 | 问题 | 证据性质 |
|---|---|---|---|
| R01 | P1 | Platform 检查为红，正式构建流程会在该检查处停止 | 已实际运行 |
| R02 | P1 | 默认 native 目录仍是旧 ABI，增量输出掩盖批次混用 | 已检查导出与复制规则；全新发布待验证 |
| R03 | P1 | 小组件读取失败后的默认值可能覆盖原配置 | 完整代码链已确认；故障注入待做 |
| R04 | P1 | 详情页强制深色字，绕过材质取色结果 | 静态策略冲突；合成画面对比度待测 |
| R05 | P1 风险 | 退出缺少总异常兜底与统一完成路径，插件耗时可超过安装等待 | 代码路径；异常 / 超时场景待验证 |
| R06 | P2 | 详情页看不见频谱时仍采集并更新频谱 | 采集条件与绘制条件不一致 |
| R07 | P2 | 首页提前返回，吞掉原生 WM_CLOSE 对应的退出事件 | 原生到托管的分支链已确认 |
| R08 | P2 风险 | 文件夹 4 秒超时没有结束底层 IO，单读锁可能长期占用 | 代码边界；重定向 / 离线目录待测 |
| R09 | P2 | 插件解压与部分同步入口仍在 UI 调用链 | 代码链已确认；卡顿程度未测 |
| R10 | P2 | 启用中插件更新失败仍删除旧备份并返回安装成功 | 代码链已确认；坏包更新场景待测 |
| R11 | P2 | 启动失败只落日志，写错误日志本身还有未兜底异常 | 静态错误路径 |
| R12 | P2 / 发布准备 | 新品牌图标尚未接入 EXE、托盘及窗口资源 | 项目资源与加载路径已检查 |
| R13 | P1 / 发布准备 | 正式版本、预览版本、文档与验收批次尚未统一 | 文件与脚本已检查 |
| R14 | P2 / 范围选择 | 自绘主要功能没有普通键盘焦点导航 | 代码确认；不能等同“没有 UIA” |

## 4. 逐项依据与建议

### R01：平台检查仍要求默认两张卡，会阻断正式构建

**依据**

- [src/GlassDesk.Core/WidgetBoardData.cs:16](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Core/WidgetBoardData.cs:16>)：`CreateDefault()` 只创建 `time.default` 一张时钟。
- [tests/GlassDesk.PlatformChecks/WidgetStoreChecks.cs:25](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/tests/GlassDesk.PlatformChecks/WidgetStoreChecks.cs:25>)：首次创建断言仍是 `Instances.Count == 2`，描述仍为时间 / 日期组合。
- 同文件损坏配置恢复与超大文件恢复检查也要求两张，见 [损坏恢复断言](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/tests/GlassDesk.PlatformChecks/WidgetStoreChecks.cs:144>) 和 [超大文件恢复断言](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/tests/GlassDesk.PlatformChecks/WidgetStoreChecks.cs:164>)；不能只修第一条。
- [scripts/build-release.ps1:24](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/scripts/build-release.ps1:24>)：正式脚本执行 PlatformChecks，非零退出即抛错，不会继续发布 / 安装器生成。

**本次结果**：`cards=1, saved=True, notice=none`，退出码 1。这说明默认文件已保存，失败点是旧产品预期，不能据此宣称存储本身损坏。

**建议**：保持当前单时钟产品方向，同步正常创建与损坏恢复的期望，并保留“文件确实保存、损坏原文备份、设置相互独立”的检查。不要删除整组检查，也不要仅为过测试把日期卡偷偷加回来。

**验收**：现有 PlatformChecks 全部执行完并通过；记录真实数量。修改后才知道有没有后续失败，本报告不预先宣称其他项全部正常。

### R02：普通编译成功掩盖了原生 DLL 来源不一致

**依据**

- [src/GlassDesk.NativeBridge/GlassDesk.NativeBridge.csproj:7](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.NativeBridge/GlassDesk.NativeBridge.csproj:7>)：默认读取 `artifacts/native/Release`，复制使用 `PreserveNewest`。
- [src/GlassDesk.NativeBridge/NativeTypographyAbi.cs:32](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.NativeBridge/NativeTypographyAbi.cs:32>)：当前托管桥要求 ABI V2 以及对应导出；旧 DLL 会明确拒绝。
- [src/GlassDesk.NativeBridge/NativeWidgetHost.cs:66](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.NativeBridge/NativeWidgetHost.cs:66>)：创建宿主前就执行 ABI 校验。

本次只读核查：

| 位置 | ABI V2 导出 | SHA-256 前 16 位 |
|---|---|---|
| 默认 native/Release | **没有** | `C32E4D86019CA8AB` |
| native-ui-preview/0.3.0-preview.2 | 有 | `24DBE5B9F05133B8` |
| 本次普通构建的 App/bin 输出 | 有，仍与 preview.2 相同 | `24DBE5B9F05133B8` |

**影响**：当前 bin 能保留较新的预览 DLL；换一个空输出目录，默认输入却仍指向旧 DLL。构建不报错与“当前源代码、桥、DLL、worker 属于同一批次”是两件事。这里没有证明本次 bin 必然启动失败。

**建议**：正式发布从当前原生源码重建 DLL 与 CaptureWorker，再发布到全新、明确命名的候选目录；记录来源与哈希，增加无窗口的 ABI 校验。正式脚本本来就有 native 重建步骤，因此不能把它描述成“脚本永远打错 DLL”；隐患在跳过该步骤、复用目录或手工打包。

**验收**：候选目录中的实际 DLL 协商 ABI V2 成功；候选 DLL / worker 哈希与本次 native 构建产物一致；不借用旧预览 bin 补文件。

### R03：读取失败后的默认组件可能覆盖恢复可读的原配置

**依据 / 调用链**

1. [src/GlassDesk.Platform/WidgetBoardStore.cs:48](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Platform/WidgetBoardStore.cs:48>)：IO / 权限错误时返回默认 board，保留原文件；备份失败的分支也返回默认 board。
2. [src/GlassDesk.App/GlassDeskCoordinator.Widgets.cs:65](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.Widgets.cs:65>)：调用方拿不到“真实读取成功 / 临时默认”的状态，一律 `boardReady=true`。
3. [src/GlassDesk.App/GlassDeskCoordinator.cs:603](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:603>)：退出时只要 boardReady 就保存当前 board。
4. [src/GlassDesk.Platform/WidgetBoardStore.cs:112](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Platform/WidgetBoardStore.cs:112>)：保存直接原子替换已有文件。

**触发条件**：启动时 widgets.json 暂时被锁或无法访问 → UI 使用默认一张卡 → 原文件后来恢复可访问 → 普通退出保存默认 board。原子替换保证 JSON 完整，却不能保证替换的内容就是用户原来的数据。备份失败时“不会覆盖原文件”的注释也没有覆盖上层退出保存。

**建议**：Load 返回明确结果状态，例如 `Loaded / Created / RecoveredWithBackup / ReadFailed`，并携带是否允许保存。ReadFailed 使用临时只读展示；恢复访问后重读或让用户明确选择重置，不能在退出时自动覆盖。新旧数据都需保留。

**验收**：只在隔离配置目录模拟首次读取失败，解除占用后退出，原 JSON 字节保持不变；用户主动重置时才写默认值。属于有意义的一次故障复核，不需要压力循环。

### R04：详情页文字颜色与可调整的透明材质冲突

**依据**

- [src/GlassDesk.App/GlassDeskCoordinator.cs:125](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:125>)：所有 Details 强制 `lightText=false`，覆盖刚算出的 LightInk。
- [src/GlassDesk.App/WidgetScene.Home.cs:49](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/WidgetScene.Home.cs:49>)：首页正常模式直接使用深色 Primary / Secondary，并未采用该材质判断。
- [src/GlassDesk.Core/Contracts.cs:73](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Core/Contracts.cs:73>)：用户仍可以把 TintOpacity 调到 0.10。
- [src/GlassDesk.Core/WidgetReadabilityPolicy.cs:11](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Core/WidgetReadabilityPolicy.cs:11>)：现有取色只是外围采样估计，本身已经声明不能当作合成画面对比度证据。

**影响**：深色壁纸 / 深色前台、低 tint 时，直接画在材质上的曲名、歌手与首页应用名存在暗上暗风险。导航和 Dock 的浅色薄膜可以帮助各自区域，不能证明其余正文都有同等底色。白色场景正常也不能推出全部桌面正常。

**方向**：先明确产品契约。要固定深色字，就给正文提供足够稳定、范围明确的浅色材质基础；要让外壳高度透出背景，就保留对应的前景色适配。不要同时允许低 tint、固定深色字，再把“真实玻璃”当作可读性保证。液态折射属于视觉表现，本身不保证对比度。

**验收**：最终候选包在白、深灰、复杂壁纸各看一次，包含最低与默认 tint；普通文字目标 4.5:1，大字号允许按对应规则评估。直接检查合成背景，不只检查颜色 token；不要求本轮录屏。[Windows 可访问文字要求](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements)。

**状态**：策略冲突已确认，当前所有场景实际不合格的比例尚未测量，不能写成“已实测全部看不清”。

### R05：退出缺少统一的失败处理，并与安装器等待预算不一致

**依据**

- [src/GlassDesk.App/GlassDeskCoordinator.cs:594](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:594>)：`async void Quit()` 设置 quitting、停 timer 后，连续 await 设置 flush、保存尾任务、加载、插件释放，外层没有 try/finally 保证完成清理。
- [src/GlassDesk.Plugins/PluginHost.cs:264](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Plugins/PluginHost.cs:264>)：每个插件 Stop 与 Dispose 各可等待 5 秒，插件逐个处理；正在执行的命令另可持有 gate 到 10 秒。
- [installer/GlassDesk.iss:61](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/installer/GlassDesk.iss:61>)：退出请求之后只等待约 10 秒，再决定安装 / 卸载失败。
- 个别文件保存错误已被局部 catch，但这不等于整个退出链不会抛错；保存失败也只记录日志后继续退出，未给用户重试 / 导出入口。

**影响**：异常可能发生在窗口已隐藏、quitting 已设为 true 之后，重试 Quit 又会被前置判断挡住。第三方插件慢退出时，安装器可能提示“尚未退出”。这是有条件的生命周期风险，不是本轮已观察到正常内置插件无法退出。

**建议**：提取可 await 的退出核心；事件入口只负责调用与兜底。统一进入退出状态、保存失败反馈、资源释放和退出完成信号；规定总预算，并与安装器协调。不要直接强杀用户进程或静默丢弃配置来通过安装。

**验收**：普通退出、写入失败、一个延迟插件各复核一次；成功时 mutex 确实释放，失败时提供明确处置且不留下不能再交互的驻留进程。系统关机 / 注销期间的保存路径另列人工待验，不将缺少实测等同必现数据丢失。

### R06：频谱不可见时仍在做进程音频采集

**依据**

- [src/GlassDesk.App/GlassDeskCoordinator.cs:321](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:321>)：仅排除 Details 的小组件页，首页与音乐详情页仍可 waveform=true。
- [src/GlassDesk.App/WidgetScene.cs:780](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/WidgetScene.cs:780>)：频谱绘制只给 Capsule / Peek 权重，Details 频谱不可见。
- [src/GlassDesk.Platform/ProcessAudioVisualizationSource.cs:40](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Platform/ProcessAudioVisualizationSource.cs:40>)：active 且正在播放就建立采集；worker 按约 25 Hz 分析。
- [src/GlassDesk.App/WidgetScene.cs:376](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/WidgetScene.cs:376>)：收到帧后仍可更新 8 根不可见柱的合成动画。
- 使用说明承诺“波浪条不可见时停止采集”，当前两处条件没有对齐。

**建议**：从实际可见区域得到同一个频谱激活条件；展开过渡需要短暂保留时，明确过渡结束点。不要更改受保护的 8 根静息条或胶囊位置。

**验收**：播放中从 Peek 转到首页 / 音乐详情，确认采集停；回 Peek 恢复。这里只能确认无必要工作存在，尚未测量具体 CPU / GPU 增量。

### R07：首页会吞掉原生关闭请求

**依据**

- [native/GlassDesk.Native/GlassDeskNative.cpp:788](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/native/GlassDesk.Native/GlassDeskNative.cpp:788>)：`WM_CLOSE` 转为事件 14，即 HostEventKind.Quit。
- [src/GlassDesk.App/GlassDeskCoordinator.cs:392](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:392>)：首页分支提前 return。
- Quit 处理在 [src/GlassDesk.App/GlassDeskCoordinator.cs:434](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:434>)，首页不会走到。

**影响**：对原生宿主 HWND 发关闭请求时，首页状态下可能无效。托盘退出与安装器自己的 IPC 走另一条链，**本项不能表述成“托盘退出也坏了”**。

**建议**：全局生命周期事件在页面指针路由前处理；页面只拦截自己的鼠标 / 滚动 / 控件事件。

**验收**：隔离实例分别处于首页、音乐、小组件页时发送一次 WM_CLOSE，三者都进入同一退出路径。

### R08：文件夹超时控制了等待者，未保证底层读任务结束

**依据**

- [src/GlassDesk.Platform/HomeDirectoryService.cs:11](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Platform/HomeDirectoryService.cs:11>)：三个系统文件夹共用一个 SemaphoreSlim。
- 同文件 [src/GlassDesk.Platform/HomeDirectoryService.cs:27](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Platform/HomeDirectoryService.cs:27>)：枚举为同步目录 IO。
- [src/GlassDesk.Platform/HomeDirectoryService.cs:37](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Platform/HomeDirectoryService.cs:37>)：外层 WaitAsync 4 秒超时；无法强行打断已卡在系统 IO 内的调用。
- Coordinator 会取消 token，但同步 IO 返回前无法检查 token 或释放单读锁。

**影响**：桌面 / 文档重定向到离线网络、慢盘时，一个读取可能长期占锁，其他正常目录也陆续超时。当前主 UI 已避开同步目录枚举，因此不能说“这个代码一定会冻住整个 UI”。

**建议**：隔离不同目录的失败，限制挂起 worker 数量，并让超时任务的后续结果有观察 / 清理路径。不要靠不断 Task.Run 或无限重试堆积工作。结果迟到不得覆盖用户刚选的新文件夹。

**验收**：一个不可用重定向目录不会阻止其他本地目录显示；关闭 / 切页后不出现旧结果回填。没有该环境时保留为风险，不编造验证成功。

### R09：插件导入有 UI 阻塞路径，生命周期超时覆盖不了同步卡住

**依据**

- [src/GlassDesk.App/SettingsWindow.cs:456](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/SettingsWindow.cs:456>)：导入从 UI 点击进入 Coordinator / PluginHost。
- [src/GlassDesk.Plugins/PluginHost.cs:317](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Plugins/PluginHost.cs:317>)：导入直接同步 Extract；[src/GlassDesk.Plugins/PluginPackage.cs:45](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Plugins/PluginPackage.cs:45>) 最多允许约 128 MiB / 2048 项，逐文件同步复制。
- [src/GlassDesk.Plugins/PluginHost.cs:227](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Plugins/PluginHost.cs:227>)：Initialize / RegisterContributions 同步调用。
- [src/GlassDesk.Plugins/PluginHost.cs:233](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Plugins/PluginHost.cs:233>)：StartAsync 先被调用，返回 Task 后才 WaitAsync；若插件在返回前同步阻塞，5 秒等待不起作用。

**建议**：包读取、校验、解压与目录准备移出 UI 调度线程，UI 只提交结果与显示进度；分清文件事务与插件激活。SDK 明确同步入口必须快速返回。进程内可信插件无法获得真正强制中止保证，不把加一个 timeout 写成沙箱隔离。

**验收**：较大本地包导入时设置窗口可响应；失败有结果，重复点击不会并发装同一包。无需现在重做进程外插件架构，但正式说明不能承诺任意插件都不会卡主程序。

### R10：插件更新失败后的“成功”和旧备份处理不准确

**依据**

- [src/GlassDesk.Plugins/PluginHost.cs:209](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Plugins/PluginHost.cs:209>)：StartAsync 内部 catch 转 Faulted，没有把失败作为异常向 ImportAsync 传播。
- [src/GlassDesk.Plugins/PluginHost.cs:327](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Plugins/PluginHost.cs:327>)：启用中的同名更新激活新插件后，不检查最终状态就删除 backup，并返回插件 ID。
- [src/GlassDesk.App/GlassDeskCoordinator.cs:577](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:577>)：返回后对 UI 统一显示安装成功。

**影响**：更新可安装但启动失败，用户看到的成功消息不够准确，能工作的旧版本也被删除。插件页后续可能显示 Faulted，所以不是“完全没有错误状态”，问题是事务结果与恢复能力。

**建议**：区分“安装成功但未启用”“更新并启动成功”“新版本启动失败”。启动失败保留旧包或提供回退；回退前遵守现有不安全卸载 / 需重启规则，不在未卸载状态直接覆盖 DLL。

**验收**：一个可运行旧插件更新成初始化失败的新版本，不会失去恢复旧版的途径；UI 明确给出失败或需要重启的结果，配置仍保留。

### R11：失败恢复仍有“点了没反应”的体验

**依据**

- [src/GlassDesk.App/Program.cs:48](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/Program.cs:48>)：bootstrap 异常只写文件后返回。
- [src/GlassDesk.App/App.cs:27](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/App.cs:27>)：启动失败记录后退出，没有用户可见提示。
- [src/GlassDesk.App/App.cs:39](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/App.cs:39>)：WriteStartupFailure 只 catch IOException，没有兜住权限错误等日志失败。
- [src/GlassDesk.App/GlassDeskCoordinator.cs:555](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:555>)：ShowSettings 的 catch 内再次直接写文件，数据目录不可写时可能在错误处理里再抛错。

**建议**：建立轻量、非递归的失败出口：安全写日志，失败则使用受控 fallback；显示一次简短提示和日志位置 / 复制诊断方式。不打印歌曲、账号或完整私人异常内容到面向用户的反馈中。

**验收**：缺失 / 不匹配 native 或数据目录不可写时，用户能知道启动失败原因类别；日志失败不导致第二次未处理异常。这里不要求引入遥测服务。

### R12：新图标还没有真正进入产品资源链

**依据**

- [src/GlassDesk.App/GlassDesk.App.csproj:3](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDesk.App.csproj:3>) 尚无 ApplicationIcon / 品牌资源项；排除 artifacts、bin、obj 后，源码图像只有两张旧设计预览。
- [src/GlassDesk.Platform/SystemIntegration.cs:111](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Platform/SystemIntegration.cs:111>)：托盘仍加载系统 IDI_APPLICATION，提示语仍为“音乐与时钟”。
- [native/GlassDesk.Native/GlassDeskNative.cpp:808](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/native/GlassDesk.Native/GlassDeskNative.cpp:808>)：宿主注册没有指定品牌窗口图标。
- 安装器使用 EXE 作为卸载显示图标，因此 EXE 未接入，安装列表也不会自动出现新品牌。

**建议**：沿用你现在的 S 形主体和青色伙伴端部，不重做方向。交付真正的品牌母版、透明 PNG、Windows ICO 和适合小尺寸的托盘版本；配置 EXE、托盘、设置窗口和安装器实际入口。品牌颜色不自动替换受保护的设置蓝色 accent。

**小尺寸审美审查**：大图看得到两只眼睛，不能证明 16 px 托盘仍能分开。保留 S 主轮廓；必要时托盘变体简化青色端部和眼睛，不靠锐化 / 阴影补细节。聊天截图不能当作已确认的透明母版；白色背景是否属于文件像素要在资源阶段检查，不在本报告臆测。

**验收**：16 / 20 / 24 / 32 / 48 / 256 px 检查轮廓与两眼，浅 / 深任务栏各看一次；安装后查看 EXE、开始菜单、托盘、设置和卸载列表。参考：[Windows 应用图标指南](https://learn.microsoft.com/en-us/windows/apps/design/iconography/app-icon-design)，作用是约束小尺寸轮廓、简化与光学面积，不要求模仿 Windows 或旧品牌图案。

### R13：正式发布身份、文档与证据需要按当前版本重封口

**依据**

- [Directory.Build.props:8](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/Directory.Build.props:8>)：默认 Version=0.2.0。
- [installer/GlassDesk.iss:1](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/installer/GlassDesk.iss:1>)、[scripts/finalize-release.ps1:9](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/scripts/finalize-release.ps1:9>)、[scripts/package-source.ps1:3](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/scripts/package-source.ps1:3>)：正式版本 / 产物路径硬编码 0.2.0。
- [scripts/build-ui-preview.ps1:7](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/scripts/build-ui-preview.ps1:7>)、[installer/GlassDesk.UiPreview.iss:1](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/installer/GlassDesk.UiPreview.iss:1>)：预览脚本与安装器仍指向 preview.1；当前近期试用用的是另一独立目录。
- [README.md:1](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/README.md:1>)、[docs/user-guide.md:27](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/docs/user-guide.md:27>)：仍含旧的小展开、陪伴移动与配置版本描述。
- [licenses/manifest.json:3](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/licenses/manifest.json:3>)：清单 appVersion 仍为 0.1.0。BrimDeck 与 Uiverse 通知已经补入 THIRD-PARTY-NOTICES，不能误报为“没有保留许可”。
- [verification/installation-0.2.0.json:1](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/verification/installation-0.2.0.json:1>) 和 preview.1 验收属于旧字节批次，不能证明本次 UI / 首页的安装行为。

**建议**：

1. 由你确定首个公开版号，集中作为发布配置输入；应用版本、安装器版本、文件名与说明保持一致。不要把配置 schema=4 当作产品版本号改掉。
2. 保持正式版 / 预览版的 AppId、数据目录、IPC 边界清楚；需要沿用正式升级链时保留正式 AppId。不可直接把预览 EXE 改名字当正式版：编译常量与启动参数会影响身份、自启和快捷键。
3. 更新当前功能、已知限制、设置 / widgets / plugins 数据位置、迁移与卸载保留策略。当前第三方小组件贡献 gate 仍关闭，天气仍未做；与已有本地功能插件区分。
4. 按最终候选包重新产生文件哈希、源码快照与简短验收记录；旧报告保留为历史，不删也不冒充新报告。
5. 整理许可清单版本与实际组件 / 资产来源。这里是交付可追溯性问题，本报告不作法律合规认证。
6. 签名是你需要决定的发行方式，不列为“代码必坏”的理由；若暂不签名，安装说明应诚实说明，不教用户关闭系统防护。新安装包需发布 SHA-256 与可信下载入口。

**验收**：一份清单能回答“这个安装包是什么版本、来自哪一份源码、包含哪一组 DLL、有哪些已验 / 未验事项”。未提交文件本身不是运行 bug，但冻结源码时必须包含这些新增源文件；当前 source 打包脚本按文件枚举，不是仅打 Git 已追踪文件。

### R14：UIA 可调用不等于普通键盘可操作

**依据**

- [native/GlassDesk.Native/Accessibility.h:86](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/native/GlassDesk.Native/Accessibility.h:86>)：Focusable / HasKeyboardFocus 均返回 false。
- [native/GlassDesk.Native/Accessibility.h:156](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/native/GlassDesk.Native/Accessibility.h:156>)：SetFocus 返回成功但不移动焦点，GetFocus 返回空；已有 Invoke / RangeValue，不能说完全没有无障碍支持。
- 宿主没有 Tab / 方向键 / Enter / Escape 的页面焦点模型，窗口刻意 NoActivate。

**影响**：当前主要依赖鼠标或 UIA 工具；用户不能通过普通键盘完成首页应用、导航与文件夹预览操作。仅绑定显示 / 隐藏快捷键不能替代内部导航。

**建议**：保留桌面常态不抢焦点，考虑显式进入的键盘模式 / 可聚焦代理入口；进入后有可见焦点、合理顺序、Enter 执行和 Escape 退出。不要为了补 Tab 让整个悬浮组件始终抢焦点。

**验收 / 范围**：如果你决定首版暂不做，在已知限制中明确“自绘组件以鼠标为主”，不要宣称完整键盘可达；设置窗口仍需单独检查。参考：[Windows 焦点导航](https://learn.microsoft.com/en-us/windows/apps/develop/input/focus-navigation)，作用是定义焦点顺序和退出行为，不要求改成系统默认外观。

## 5. 视觉方向：这轮应当收敛什么

**保留当前已经确认的设计**：紧凑小展开、封面保留、脸左 / 时间右、下方播放控制、顶部不套额外公共框；受保护的胶囊布局与 8 根静息条；设置五类 IA、浅色主题与蓝色 accent。不要借发布审查重做整套产品。

代码层面的视觉风险与修复方向：

| 现象 / 风险 | 应做的事 | 本轮不应做的事 |
|---|---|---|
| 高透明详情页与固定深色字冲突 | 优先处理 R04，保证信息可读 | 再叠多层高光和描边来遮掩文字问题 |
| 16 px 品牌辨识不确定 | 为托盘做有根据的简化，并保留主轮廓 | 把大图机械缩小后宣称已达标 |
| 本机应用图标来源可见面积不同 | 接入新资产时按可见轮廓校准；其他入口可继续列为后续 | 为追求整齐擅自重做用户尚未要求的四枚图标 |
| 三种陪伴外观在各形态的表现范围不清楚 | 手动切一次原生 / 猫耳 / 恶魔，确认设置说明与实际一致 | 假定 Peek / Details 必须复制胶囊的全部造型 |
| 动画是否仍与 HTML 参考一致 | 由用户在最终包试一次开合、打断、退出，记录具体不连续点 | 用本次静态审查宣称“动画已完全对齐” |

当前没有运行新 UI，也没有拿到新的完整运行截图，因此**本报告不杜撰间距、像素边缘、手感已实测不合格的结论**。这些属于下面的短人工清单。

## 6. 最终候选包的短人工清单

这里只需要一轮有目的的普通试用，不要求录屏、千次循环或长时间压力测试。最好针对新安装后的同一批次执行。

| 路径 | 最低确认内容 |
|---|---|
| 首次启动 / 再启动 | 进程与托盘只有一个，能打开设置；错误能恢复 / 看见；无开发 SDK 的另一台机器能启动 |
| 配置与页面记忆 | 改一个设置、切到小组件、收起再展开、退出再启动；页面与配置保留 |
| 时钟 / 音乐三态 | 无音乐、播放、暂停五秒、恢复播放；Peek 封面始终正确，按钮不误触成展开 |
| 短工作区 | 100% 与一个高 DPI / 短工作区各看一遍；封面、控制与弹层不裁切，实际点击跟显示一致 |
| 玻璃与文字 | 白 / 深 / 复杂背景，默认与最低 tint；液态关闭、开启和失败回退 |
| 首页 / 文件夹 | 应用未安装有反馈；空目录、读取失败有状态；预览点击选中文件、打开文件夹正常 |
| 减少动态 / 隐藏 / 全屏 | 减少动态立即稳定；隐藏后停止不必要工作；全屏进出恢复正确 |
| 安装 / 升级 / 卸载 | 程序正在运行时更新能保存退出；普通数据不被覆盖；卸载后数据保留策略正确 |
| 品牌与版本 | 开始菜单、托盘、设置、卸载入口图标与名称一致；版本号一致 |

**待验不等于缺陷**：干净系统、混合 DPI、系统关机保存、签名信誉和当前 native GPU 表现，本轮都没有新证据；旧记录不能填成通过。

## 7. 建议修复顺序与交付粒度

1. **A 批：恢复可验证性和数据安全。** R01、R02、R03、R05。产物：绿色现有普通检查、正确原生批次、隔离配置恢复证明、退出结果记录。不要同时扩大功能范围。
2. **B 批：修运行行为。** R04、R06、R07；再根据首版范围处理 R08–R11。产物：相应问题的单次复核记录与一份候选试用目录。
3. **C 批：品牌与正式交付。** R12、R13；明确 R14 是否延后。产物：资源源文件、版本配置、完整安装包、哈希与来源清单、当前功能和已知限制说明。
4. **最后由你试上述短清单**，反馈后只修有证据的问题。你确认当前候选后再冻结正式批次；不在本轮擅自发布。

没有必要现在重写渲染架构、改框架、加联网天气、实现完整 AI 或上插件市场。这些不能替代关闭现有问题。

## 8. 供你审查的范围选择

- [ ] 默认继续单时钟，不恢复日期卡；按当前设计修 R01 的旧断言。
- [ ] 确定公开版号以及是否接受暂不签名；签名不影响先修 A / B 批。
- [ ] 首版是否允许普通用户导入第三方进程内功能插件；若允许，R09 / R10 建议一起修；第三方“小组件贡献”仍保持关闭。
- [ ] 普通键盘操作 R14 本轮做，或明确作为已知限制延后。
- [ ] 新品牌图案保持当前方向，后续只做资源化与小尺寸变体，设置 accent 不联动改变。

以上是待审清单，不是已经替你作出的变更授权。本轮没有执行修复。

## 9. 参考网页及用途

| 网页 | 用途 | 不应据此推导的结论 |
|---|---|---|
| [Windows 可访问文字要求](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements) | R04 的文字前景 / 背景与对比度验收依据 | 不证明仅靠外围采样就能测出真实玻璃合成对比度 |
| [Windows 应用图标指南](https://learn.microsoft.com/en-us/windows/apps/design/iconography/app-icon-design) | R12 的轮廓、细节、小尺寸及背景适应检查 | 不要求复制微软图标，不否定用户当前 S 形设计 |
| [Windows 焦点导航](https://learn.microsoft.com/en-us/windows/apps/develop/input/focus-navigation) | R14 的键盘路径与自绘控件焦点设计 | 不要求常态悬浮窗口抢焦点，不等同已有 UIA 没价值 |
| [Fluent 2 Typography](https://fluent2.microsoft.design/typography) | 字体层级、标准文字对比度的辅助核对 | 不要求替换现有字体路线、统一成另一套外观 |

这些网页用于解释设计 / 验收依据；代码缺陷的证明来自上面的本地源文件和本次日志。本轮没有上传项目到外部扫描服务。

