# 当前代码审查（首页 / 陪伴 / 运行修复批次）

日期：2026-10-22  
审查基线：分支 `ui/final`，HEAD `8891117`，**审查对象包含当前磁盘上的未提交实现**。  
审查性质：只读代码审查 + 编译 / 测试 / 一次首页实际运行截图；本轮只新增本报告，没有修改产品源码。  
结论：**代码能编译，主要现有测试通过，首页已经真实可运行，但还不建议直接冻结为正式候选。** 有 2 个 P1 级可见性/可用性问题、1 个发布测试不稳定问题，以及若干首页缓存和交付一致性问题需要先确认。

---

## 1. 实际执行结果

| 检查 | 结果 | 说明 |
|---|---|---|
| `dotnet build src/GlassDesk.App` Release | 成功，0 警告 / 0 错误 | 使用 preview.2 native 输入 |
| Core `--no-stress` | **303 项通过** | 含 `HomePresentationChecks` 的 6 条几何检查 |
| Platform | **66 项通过**，但出现过一次失败 | 见 P1-04，属于测试不稳定，不是稳定红 |
| Plugin `--no-stress` | **60 项通过** | 回退 / 生命周期检查通过 |
| NativeBridge | **6 项通过** | 包含实际加载 native 的 ABI 协商 |
| Audio | 退出码 0 | 未单独统计项目数 |
| 首页实际运行 | 成功 | 首页、导航展开、文件夹气泡、UIA 控件均可工作；见证据截图 |
| 真实目录 / 白底深底 / 键盘 / 安装器 | **未执行** | 本报告不把这些写成通过 |

运行证据：

- 首页：[current4-01-home](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/artifacts/audit/current-review-2026-10-22/screens/current4-01-home-1-GlassDesk-UI-Preview-0-3-0-preview-3-540x768.png>)
- 导航展开：[current4-02-nav](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/artifacts/audit/current-review-2026-10-22/screens/current4-02-nav-1-GlassDesk-UI-Preview-0-3-0-preview-3-540x768.png>)
- 文件夹气泡：[current4-03-popover](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/artifacts/audit/current-review-2026-10-22/screens/current4-03-popover-1-GlassDesk-UI-Preview-0-3-0-preview-3-540x768.png>)

---

## 2. 问题总表

级别：P1 = 建议正式候选前关闭；P2 = 功能或交付高风险，需明确是否接受；P3 = 卫生问题 / 后续清理。

| ID | 级别 | 问题 | 状态 |
|---|---|---|---|
| P1-01 | P1 | 首页返回入口只有不可见命中区，没有可见返回箭头 | 已确认代码路径 |
| P1-02 | P1 | 首页应用标签在深色外壳上仍是深色文字，对比度不足 | 已由运行截图确认 |
| P1-03 | P1 | 当前实现和新文件全部未提交，正式候选无法按当前状态追溯 | 已确认 Git 状态 |
| P1-04 | P1 | Platform 检查存在非确定性失败，可能卡住正式发布脚本 | 已实际复现一次 |
| P2-01 | P2 | Home 不是首次启动默认页，且返回入口不可见时 Home 基本不可达 | 产品契约待确认 |
| P2-02 | P2 | 文件夹气泡按 `HomeFolderPreview.ToString()` 缓存，目录内容变化后可能显示旧列表 | 已确认代码路径 |
| P2-03 | P2 | 本机应用图标直接按 52 DIP 绘制，实际可见面积不一致 | 已由运行截图确认 |
| P2-04 | P2 | 退出保存失败后取消退出，会留下被停掉的计时器和 outside 观察器 | 已确认代码路径 |
| P2-05 | P2 | 首页 / 气泡缓存遗漏次要 ink 与部分主题字段 | 已确认代码路径，影响较低 |
| P2-06 | P2 | HomeVisual 没有自动化测试，几何测试不能覆盖真实合成和缓存 | 测试覆盖缺口 |
| P2-07 | P2 | 文件夹底层同步目录 IO 仍无法真正取消；超时后 worker 可长期占用 | 已知边界，仍未完全关闭 |
| P2-08 | P2 | 第三方插件同步入口被移到线程池，可能破坏依赖 UI 线程的旧插件 | 回归风险，测试未覆盖真实第三方 |
| P3-01 | P3 | HomeVisual 的路径资源、隐藏后的 gaze 偏移存在小型卫生问题 | 代码卫生 |
| P3-02 | P3 | 气泡 / 文件夹标签 / help 的缓存 key 不完整，主题变化时可能残留旧色 | 低 |
| P3-03 | P3 | `HomePopupBody` 暴露为可访问控件但没有对应动作 | 低 |
| P3-04 | P3 | 新首页视觉仍缺少完整的白底 / 深底 / 低 tint / 短窗口人工验收 | 待人工验收 |
| P3-05 | P3 | R12 品牌资源、R13 版本文档、R14 键盘焦点仍未关闭 | 旧发布审查遗留，本轮未处理 |

---

## 3. P1 问题

### P1-01 首页返回入口没有可见 UI

**证据**

- [WidgetScene.cs:1517](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/WidgetScene.cs:1517>) 只在非 Home 页添加了 `WidgetActions.HomeBack` 的 44×44 命中区：`new(WidgetActions.HomeBack, "返回首页", 12, 6, 44, 44)`。
- 全仓库检索 `HomeBack` 只找到 Coordinator 的指针/Invoke 分支和这条 AccessibleControl；`HomeVisual`、`DetailsChromeVisual`、`DrawDetails` 都没有绘制返回箭头或 hover 状态。
- [docs/home-repair-2026-10-22.md:27](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/docs/home-repair-2026-10-22.md:27>) 声明“两个子页加独立返回首页箭头”，与当前实现不一致。

**影响**

- 从音乐页 / 小组件页看，左上角是空白的；普通鼠标用户不知道存在可点击的 Home 返回入口。
- 顶部陪伴把手只负责收回胶囊，不返回 Home；Chrome Dock 也没有 Home 项。因此**一旦离开 Home，普通用户几乎没有可见方式回去**。
- UIA 用户仍可找到“返回首页”，但不能把无障碍属性当成视觉入口已经实现。

**建议 / 验收**

- 在非 Home 详情页绘制可见返回箭头，提供 hover / pressed / 减少动态状态；至少保持 44×44 命中。
- 截图验证音乐页和小组件页的箭头可见；鼠标点击、UIA Invoke、键盘（如果未来支持）各验证一次。
- 如果确实不想要箭头，必须同时提供其他可见的 Home 入口，并更新文档，不能保留“只有不可见热点”的状态。

### P1-02 首页应用标签在深色外壳上对比度不足

**证据**

- [WidgetScene.Home.cs:52](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/WidgetScene.Home.cs:52>) 的应用标签 ink 仍依赖材质判断：
  `high ? _text : TypographyCatalog.Ink(_promoteSecondary ? Primary : Secondary, _lightText)`。
- [HomeVisual.cs:203](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeVisual.cs:203>) 直接把这些标签画在 `_apps` 层上，没有导航 / Dock 那样的浅色材质底。
- [TypographyCatalog.cs:61](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Core/TypographyCatalog.cs:61>) 的深色 Secondary 是 `#48515b`，Primary 是 `#20252c`。
- 本次运行截图里窗口外壳是深灰色，“网易云音乐 / Edge / Terminal / 照片 / 记事本 / 计算器”都是暗字直接压在暗底上；不是微弱的边缘问题。

**影响**

- 首页最核心的入口名称在默认深色外观 / 低 tint 场景下难以阅读，属于可读性 / WCAG 风险。
- `home-repair` 文档声称 F01 已处理浅色可见度；当前截图说明至少 Home 的应用标签还没有达到“稳定可读”。

**建议 / 验收**

- 不要只依赖外围取色推测。给 Home 应用标签一个明确的浅色/深色 label 底，或让 Home 自己的材质决定标签 ink；如果保留直接绘制，必须在最终合成背景上验证。
- 至少检查：白色壁纸、深灰壁纸、复杂壁纸、默认 tint、最低 tint（0.10）、高对比、液态玻璃开启 / 关闭。
- 目标：普通正文 4.5:1，大字号按对应规则；验收要基于合成画面而不是颜色 token。

### P1-03 当前实现未提交 / 未冻结

**证据**

- `git status --short` 显示：`HomeVisual.cs`、`HomePresentationLayout.cs`、`HomeDirectoryService.cs`、`HomeIconLoader.cs`、`PeekCompanionVisual.cs`、`SkipGlyphVisual.cs` 等全部未跟踪；Coordinator、WidgetScene、PluginHost、Native C++ 等也仍有未提交修改。
- HEAD 仍是 `8891117`，即上一轮首页原型提交，不包含当前 HomeVisual / 目录服务 / 插件修复 / 原生物改。

**影响**

- 当前“基本做完”的代码没有进入 Git 基线。任何用 HEAD 生成的源码包、差异审查或回滚都不等于当前磁盘。
- 发布 / 交接时无法回答“这个候选来自哪一份源码”；新增文件若被遗漏，`HomeVisual` 等直接编译失败。

**建议**

- 在继续设计迭代前，先按功能拆成可审查提交：Home layout/presentation、HomeVisual、home directory service、icons、runtime fixes、native overlay、plugin fixes。
- 提交后重新生成候选哈希和源码快照；不要把“当前 bin 能跑”当作源码已冻结。

### P1-04 Platform 检查非确定性失败

**证据**

- 一次实际运行 `dotnet run --project tests/GlassDesk.PlatformChecks/GlassDesk.PlatformChecks.csproj -c Release --no-restore -m:1` 退出码 1，失败项为：
  `FAIL: serialized atomic widget writes expose complete JSON to concurrent readers and remove temporary files`
- 紧接着连续 3 次运行均为退出码 0，当前一次记录 `"Passed": 66`。
- 该检查位于 [WidgetStoreChecks.cs:194](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/tests/GlassDesk.PlatformChecks/WidgetStoreChecks.cs:194>) 的 `AtomicChecksAsync`：读者以 `FileShare.ReadWrite | FileShare.Delete` 打开，写入者并发 `File.Replace`；读者只捕获 `IOException / JsonException` 并记录错误，测试要求错误队列为空。

**影响**

- 正式发布脚本会在 PlatformChecks 非零时停止（见 `scripts/build-release.ps1` 的检查流程）。一次偶发失败就会阻断发布，或被误判为产品回归。
- 这也说明测试断言对 Windows 文件替换的瞬时状态没有容忍；不能把“单次绿”当成流程稳定。

**建议 / 验收**

- 要么让并发读者对 `FileNotFoundException` / `DirectoryNotFoundException` 做有界重试并只验证最终可解析，要么调整实现/测试契约，明确“替换期间允许短暂不可打开”。
- 连续运行至少 20 次并记录失败率；发布脚本使用这份稳定性证据，而不是一次通过。

---

## 4. P2 问题

### P2-01 Home 不是首次启动默认页，且返回入口不可见时 Home 基本不可达

**证据**

- [Contracts.cs:49](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Core/Contracts.cs:49>)：`LastDetailsPageId` 默认是 `DetailsPageController.Music`。
- [DetailsPageController.cs:13](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Core/DetailsPageController.cs:13>)：版本小于 4 的用户也落到 Music。
- 当前 [GlassDeskCoordinator.cs:141-154](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:141>) 不再强制第一次大展开进入 Home，而是保留记忆页。
- 结合 P1-01 没有可见返回箭头，新装 / 旧数据用户第一次展开会停在音乐页，Home 基本找不到。

**判断**

这不是必然的代码错误，而是产品契约选择：如果“首页”是新的默认落地页，那么默认值 / 迁移需要改；如果坚持“记忆上次页面”，必须同时提供可见 Home 入口。请先决定一个，不能两者都不做。

**建议**

- 方案 A：默认 / v1-v3 迁移到 Home；老用户保留记忆。
- 方案 B：保持 Music 默认，但增加可见 Home 入口（或 Dock/导航中的 Home 项）。
- 验收：全新数据目录、旧 v3 数据、已有 v4 home / music / widgets 数据各启动一次，确认预期页和回程。

### P2-02 文件夹气泡缓存可能显示旧目录内容

**证据**

- [HomeVisual.cs:287](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeVisual.cs:287>)：
  `string key = $"{selected}/{preview}/{pop}/{pressed}/{_foreground}/{_background}/{dpi}";`
- [WidgetScene.Home.cs:9-10](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/WidgetScene.Home.cs:9>) 的 `HomeFolderPreview` 是普通 record，`Entries` 是 `IReadOnlyList<HomeFolderEntry>`；自动生成的 `ToString()` 只把集合打印成类型（例如 `System.Collections.Generic.List...` / `System.String[]`），**不包含条目名、路径、IsFolder**。
- `_popupInk.Set(...)` 只在 key 变化时执行（[HomeVisual.cs:288-295](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeVisual.cs:288>)）。

**影响 / 复现**

1. 打开“桌面”气泡，关闭（不重启应用）。
2. 在桌面新增 / 删除 / 改名一个文件。
3. 再次打开同一个文件夹；新 `HomeFolderPreview` 的 Name / Path / Status / Message 都没变，key 与旧值相同，`_popupInk` 不会重绘，画面仍是旧条目。
4. 更糟的是 `AddHomeControls` 会按新的 `Entries` 创建点击目标：显示旧文件名，但点击可能按新条目执行，出现显示和动作不一致。

**建议**

- 缓存 key 使用确定性的内容摘要：`selected + status + message + name + path + ordered entries (name/path/IsFolder) + pressed + foreground/background/secondary/high/dpi`。
- 或者每次 preview 内容变化就显式 `InvalidatePopup()`，不要依赖 record 的默认 ToString。
- 验收：同一文件夹关闭 → 修改目录 → 重开；气泡显示和点击目标都必须是新内容。

### P2-03 本机应用图标可见面积不一致

**证据**

- [HomeIconLoader.cs:30-38](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeIconLoader.cs:30>) 直接取应用的 `DisplayInfo.GetLogo` / `StorageFile` 缩略图。
- [HomeVisual.cs:211-218](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeVisual.cs:211>) 一律按 52×52 的同一画布绘制，不按实际可见边界归一化。
- 本次截图里 NetEase / Edge 的图形几乎填满 52 DIP，而 Terminal / 照片 / 记事本 / 计算器明显偏小、视觉重量偏轻；文件夹 / 导航卡的图标与它们不成比例。

**影响**

- 首页应用图标看起来像来自不同设计系统，削弱“统一槽位 / 一眼识别”的目标；小图标在大图标旁边会显得发灰、发空。
- 这也与之前发布审查中的“本机应用图标来源可见面积不同”一致。

**建议**

- 对每个真实图标做 alpha / 内容边界归一化，或在应用图标外统一加同一尺寸的材质托底；更好的做法是逐步替换为专为本项目绘制的 6 个图标资产。
- 验收：截图对比 6 个图标的可见轮廓、光学校准和 32×32 缩略图；不要只看“图片是否存在”。

### P2-04 取消退出后 outside 观察器 / 计时器不会完全恢复

**证据**

- [GlassDeskCoordinator.cs:599-640](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:599>)：`Quit()` 开始先 `StopRuntimeTimers()` 和 `outside.Update(null)`。
- 如果保存失败且用户在 `ConfirmExitWithoutSaving()` 选择“否”，代码只是 `quitting=false`、`ShowSettings(); Render(); UpdatePlugins(); Changed?.Invoke(); return;`（[GlassDeskCoordinator.cs:616-620](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/GlassDeskCoordinator.cs:616>)）。
- 没有重新调用 `UpdateOutsideGeometry()` / 恢复 `outside`；`StopRuntimeTimers()` 停掉的 `motionTimer / cursorTimer / textToneTimer / mediaStateTimer` 等也没有统一重启。

**影响**

- 保存失败、用户取消退出后，应用看似还在，但点击外部区域不再收起详情，部分轮询 / 动画状态也可能停摆。属于“取消退出后半残”生命周期问题。
- 这不是首页专属，但和最近改动的退出保护属于同一批。

**建议**

- 把“退出尝试”和“退出完成”拆开：在确认不退出时恢复 `outside.Update(...)`、`UpdateContentTimers()`、`WakeMotion()`；或者先做可取消的保存，再决定停止运行时观察器。
- 验收：只读数据目录触发保存失败 → 选择“否” → 点击外部、切页、悬停、退出菜单都仍正常。

### P2-05 首页 / 文件夹缓存遗漏主题字段

**证据**

- `_popupKey` 没有 `secondary`、`highContrast`；`_helpKey` 只有 `helpName/foreground/dpi`；`_folderLabels[i]` key 只有 `folderNames[i]/foreground/dpi`（[HomeVisual.cs:236-250](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeVisual.cs:236>)）。
- 这些 surface 的颜色实际使用了 `secondary` / `background` / high contrast 分支。

**影响**

- 主题、辅助 ink 或高对比变化后，气泡文件图标、导航 help、文件夹标签可能残留旧颜色；不会崩溃，但会出现局部不跟主题。
- 和 P2-02 是同一类缓存设计问题，建议统一用明确的“外观版本 / palette revision”作为 key。

### P2-06 HomeVisual 没有自动化测试

**证据**

- `HomePresentationChecks` 只校验 `HomePresentationLayout` 几何（视口、Dock、命中矩形、滚动可达、图标宽度），不实例化 `HomeVisual`。
- 没有测试覆盖：气泡缓存 key、应用标签 ink、返回入口可见性、文件夹内容刷新、高对比 / 减少动态、隐藏后恢复。
- 当前 Core 303 全绿不能证明这些合成层行为。

**建议**

- 至少抽出可测试的 key builder / layout state 为纯函数，覆盖“同路径不同 entries”的缓存测试；UI 层用一次性 QA 截图 / UIA 验收补足。
- 不需要为了测试引入全套 UI 测试框架，但不要用几何测试代替实际合成验证。

### P2-07 文件夹底层同步目录 IO 仍不可取消

**证据**

- [HomeDirectoryService.cs:27-39](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Platform/HomeDirectoryService.cs:27>)：`Task.Run(() => _read(index))` 执行同步 `DirectoryInfo.EnumerateFileSystemInfos`，外层 `WaitAsync(_timeout, cancellation)` 只能让调用者不再等待。
- `_reads[index]` 最多保留一个挂起任务；如果底层 OS 调用卡住，后续点击会继续等同一个任务，直到超时。不同目录已隔离，UI 线程不被阻塞，这是一个改进；但它不是“底层读取被取消”。
- 本次 `HomeDirectoryChecks` 只验证了“等待者取消 / 重复请求复用”，没有离线重定向目录、慢盘的实测证据。

**建议**

- 正式说明中保留“目录读取可能超时，重试会再次等待”的限制，不要宣称底层 IO 可取消。
- 若要彻底解决，使用可取消的 Win32 枚举或独立 worker / 进程，并限制挂起 worker 数量；保留迟到结果隔离。

### P2-08 第三方插件调用线程变化是潜在回归

**证据**

- [PluginHost.cs:244-255](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Plugins/PluginHost.cs:244>) 对 `entry.Factory is null` 的第三方插件通过 `Task.Run(invocation)` 执行构造、初始化、Pause/Stop/Dispose/命令。
- 内置插件仍走同步路径；`UpdateSceneAsync` 还会为每个插件发布 scene，第三方插件回调可能从线程池线程进入。
- `RequireUnloadedAsync` 在 [PluginHost.cs:314-320](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Plugins/PluginHost.cs:314>) 中做最多 3 次 `GC.Collect` / 等待；这段代码可能被 UI 调用链等待。

**影响**

- 这是有意的防 UI 阻塞改动，但旧插件可能假定初始化 / Pause / Stop 在 UI 线程；现有 PluginChecks 是新旧 SDK 的契约测试，不能覆盖真实第三方插件的线程亲和性。
- 如果插件 SDK 没有明确“不再保证 UI 线程”，正式版可能引入第三方兼容性回归。

**建议**

- 在 `docs/plugin-development.md` 中明确线程契约（如果已写，补充具体入口）；对内置 / 第三方分别验证一次线程 ID。
- 不要把该改动描述成“没有风险”；至少做一次真实旧插件加载、命令、暂停、卸载的人工测试。

---

## 5. P3 / 卫生问题

### P3-01 HomeVisual 路径资源与隐藏后的 gaze

- [HomeVisual.cs:22](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeVisual.cs:22>) 的 `_paths` 保存 `CompositionPath`；[HomeVisual.cs:70-78](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeVisual.cs:70>) 在 geometry 变化时替换 `geometry.Path`，旧 `CompositionPath` 没有显式 `Dispose`；`_paths.Clear()` 也不释放值。属于小量 COM / 内存卫生问题。
- [HomeVisual.cs:154-159](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeVisual.cs:154>) 在 `visible=false` 时提前返回；`StopMotion()` 停掉动画但不会把 `_gaze.Offset` 复位。窗口隐藏再显示后，眼睛可能保留上一次偏移，直到指针再次移动。建议隐藏时 `_gaze.Offset = Vector3.Zero`。

### P3-02 缓存 key 不完整

- `_helpKey` 没有 `secondary`；`_folderLabels` key 没有 `highContrast/background`；`_popupKey` 没有 `secondary/highContrast`。这些是低风险局部旧色，和 P2-02 一起修。

### P3-03 `HomePopupBody` 是可访问控件但没有动作

- [WidgetScene.Home.cs:151](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/WidgetScene.Home.cs:151>)：`HomePopupBody` 被加入 AccessibleControl，但 `_homeActions` 没有映射；`RouteHomePointer` 只做特殊 case 返回。UIA 里会暴露“文件夹预览”按钮，Invoke 没有明确结果。建议给它明确 role / 不可 Invoke，或从可访问树中移除，仅保留视觉背景。

### P3-04 首页视觉验收范围

- 本次只看了默认深色外观的首页 / 导航 / 气泡。尚未覆盖：白底、低 tint、液态玻璃开启 / 失败回退、320 宽、300 / 360 / 400 高、短窗口滚动、高对比、减少动态、真实空目录 / 权限错误目录、文件点击 Reveal。
- 这些不是“已确认不合格”，而是当前测试证据不足；不要用本次截图宣称全部视觉已验收。

### P3-05 正式发布边界仍未关闭

以下问题在旧发布审查 `docs/release-readiness-audit-2026-10-22.md` 已列出，本次首页 / 运行修复没有关闭它们；如果本轮目标只是试用手感，可以保留为后续，但**不能把当前树称为正式候选完成**：

- **R12 品牌资源**：新品牌图标尚未接入 EXE、托盘、设置窗口与卸载入口；Home 当前使用系统应用图标，不是最终的 6 个原创首页图标。
- **R13 版本与文档**：`Directory.Build.props` / 安装器 / 发布脚本仍是旧版本约定；本报告没有替你做版本决策。
- **R14 键盘**：自绘首页仍以鼠标 / UIA 为主；没有进入式键盘焦点模型。UIA 可调用不等于普通键盘可操作。
- 这三项属于交付范围问题，不应被核心检查 303 / 平台 66 全绿掩盖。

---

## 6. 已确认修复 / 不应重复开发的部分

以下是本次查看代码后可以确认已有有效改动的部分（仍建议按验收路径复核）：

- **R01 旧 Platform 断言**：`WidgetStoreChecks` 已同步单时钟默认值；正常、损坏、超大文件恢复的期望统一为一张时钟。当前 66 项通过（忽略 P1-04 的并发 flake）。
- **R03 默认值覆盖保护**：`PreferenceStore` / `WidgetBoardStore` 有 `_canSave` 保护，读失败 / 备份失败时拒绝保存；新增隔离目录测试验证解锁后不能覆盖原字节。
- **R04 详情页强制浅色字**：当前 `textToneTimer` 不再强制 Details 为 dark ink；Home 应用标签仍存在本报告 P1-02 的独立问题，不应重复改详情页策略来“顺带”修 Home。
- **R06 不可见频谱采集**：`UpdateContentTimers` 的 waveform 条件改为 `controller.Form != Details`；Details 页不再启动频谱采集。
- **R07 首页吞 WM_CLOSE**：`OnPointer` 现在先处理 `Quit / OpenSettings`，再进入首页路由；关闭请求不会再被 Home 分支提前吞掉。
- **R08 / 文件夹隔离**：`HomeDirectoryService` 已按目录隔离、限制每个目录一个读取，并让 UI 线程走异步等待；底层 IO 仍不可取消（P2-07）。
- **R09 / R10 插件导入与回退**：解压和目录移动转到工作线程；更新失败有旧包恢复路径；`RequireUnloadedAsync` / quarantine 保留仍在执行的插件。PluginChecks 60 项通过，但真实第三方人工验证仍缺。
- **R11 错误出口**：`FailureReporter` 已提供安全日志 fallback 和一次提示；启动失败 / 设置打开失败不再裸写文件。

---

## 7. 建议修复顺序

1. **先修 P1-01 / P1-02**：可见返回入口、Home 应用标签对比度。这两个直接影响首页是否可用 / 可读。
2. **冻结前处理 P1-03 / P1-04**：提交当前实现，修测试 flake 并连续验证发布脚本。
3. **再决定 P2-01**：Home 是否默认落地；如果坚持记忆页，必须保留可见 Home 回程。
4. **P2-02 / P2-05 一起修缓存 key**：一个内容摘要 builder，覆盖 entries / theme / secondary，避免显示和点击不一致。
5. **P2-03 / P2-04 / P2-06 / P2-07 / P2-08** 按风险排：视觉归一化、退出取消恢复、HomeVisual 测试、目录 IO 限制、插件线程契约。
6. **最后再做一次短人工验收**：白 / 深 / 低 tint 背景、短窗口、真实目录、关闭与重开、Home ↔ Music ↔ Widgets 往返、隐藏 / 全屏 / 减少动态。

---

## 8. 需要你决定的事项

- [ ] 首次启动默认进入 Home，还是继续记忆音乐 / 小组件？（P2-01）
- [ ] Home 的应用标签是加 label 底，还是统一改成浅色 / 深色单套 ink？（P1-02）
- [ ] 是否接受用本机真实应用图标直接显示，还是本轮就换成设计好的 6 个图标资产？（P2-03）
- [ ] 第三方插件是否允许继续导入并使用进程内执行；如果允许，需要人工验证 P2-08 的线程契约。
- [ ] 正式候选是否接受当前未提交实现作为源码基线，还是先按功能拆分提交后再冻结？（P1-03）

---

## 9. 说明

- 本报告没有修改产品源码、测试源码或发布脚本；只新增了本文件与审查截图。
- “实际运行成功”只覆盖首页、导航展开、文件夹气泡和 UIA 控件的一次会话；没有把一次 UI 自动化的成功扩展到安装包、真实目录、键盘、GPU 或长期驻留。
- 本次审查中 UI 自动化有一次胶囊点击没有展开，重试后成功；没有证据表明这是应用输入缺陷，因此未列为问题，但后续 UI 验收应使用固定窗口坐标 / 重试保护，避免把自动化抖动误判为产品 bug。
