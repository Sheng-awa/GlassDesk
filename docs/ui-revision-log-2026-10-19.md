# UI 修订记录（2026-10-19，第二轮反馈）

依据：用户对 Phase 1–4 实际截图的反馈。只改细节，不动已确认的结构。

## 反馈 → 处理

| 用户反馈 | 处理 |
|---|---|
| 图一：“回首页”只需要点击陪伴球，不要其他东西 | 删除顶部悬浮的“回首页”文字牌；首页把手保留，点击仍回胶囊。UIA 名称保留（不可见） |
| 图一：底部导航没做好，颜色统一，不要黑色 | Dock 从 `#202328` 实心黑条改为**自适应半透明材质**：深底用白色薄膜 + 白色图标，浅底用墨色薄膜 + 墨色图标；不再有黑色底 |
| 图一：不要文字，直接表示 | Dock 去掉选中项文字、悬停名字牌；只保留图标 + 一个 36×36 选中圆。顶部把手也去掉文字 |
| 图二：不要这么多文字 | 小组件页删除“我的组件”标题；添加/编辑改为**纯图标按钮**（+ / 铅笔）；卡片不再显示“时间/日期”标签和 UTC 长说明 |
| 图二/图三：时间小组件改成图 3 样式，其他小组件暂时不要 | 最终按反馈做成**苹果表盘式时钟**：白色圆形表盘、60 格刻度、黑色时针/分针、红色细秒针、中心点；原苹果标位置换成**小号数字时间**（约 9.5 DIP）。日期卡从默认板、添加菜单和显示层中移除；旧 date 数据保留但不显示 |

## 代码修改

### Core
- `DetailsChromeLayout.cs`：选中胶囊 88×36 → **36×36 圆**（`DockActiveWidth/Height=36, Radius=18`）。
- `WidgetBoardData.cs`：
  - `CreateDefault()` 只创建时间卡。
  - 新增 `TimeOnlyForPresentation(state)`：展示层只保留时间实例；保存数据不动，便于以后恢复其他小组件。
  - `Pack()`：单卡时水平居中；多卡保持原网格。

### App
- `DetailsChromeVisual.cs`：
  - 新增 `SetLightInk(bool)`：Dock 随材质变亮/变暗。
  - 新增 `SetCompanionColor(uint)`：顶部把手使用用户当前陪伴色（默认仍是 `#202329`）；眼睛颜色自动取对比色。
  - Dock 背景改为半透明薄膜（深底白 11% / 浅底墨色 8%），无黑色。
  - 选中项改为 36×36 圆 + 图标，**无文字**。
  - 删除“回首页”与 Dock 悬停文字的实际绘制（标签层保留但不再显示）。
- `WidgetScene.cs`：
  - `SetTextTone` 同步调用 `_chrome.SetLightInk(light)`；
  - `SetCompanionState` 同步 `_chrome.SetCompanionColor(...)`。
- `WidgetScene.Board.cs` / `GlassDeskCoordinator.Widgets.cs`：
  - 小组件页高度改为**按内容自适应**（最小 324、最大 512；当前单卡约 348），不再固定 512；
  - 展示层使用 `TimeOnlyForPresentation`，已有日期卡不会突然消失于数据，只是暂不显示。
- `WidgetBoardRenderer.cs`：
  - 工具栏去掉标题；添加/编辑/完成/取消全部改为 32 DIP 图标按钮；
  - 添加菜单只保留“时间”；
  - 时间卡画苹果表盘式时钟：白盘 + 60 格刻度 + 时分针 + 红秒针 + 中心点；顶部原苹果标位置用小号数字时间；编辑态仍显示卡片面；
  - 其他 provider（如旧日期卡）仍走原通用绘制逻辑。
- 不涉及：native、设置窗口、插件、正式配置。

## 验证

- `dotnet build src/GlassDesk.App/... -c Release`：成功，0 警告 0 错误。
- `dotnet run tests/GlassDesk.Core.Tests ... -c Release --no-restore`：**230 checks passed**。
- 预览启动 + 真实指针点击 + 窗口截图（150% DPI）：
  - `artifacts/qa/ui-final3/screens/1-capsule-324x78.png`
  - `artifacts/qa/ui-final3/screens/2-music-540x714.png`（Dock 无文字、无黑条）
  - `artifacts/qa/ui-final8/screens/final-widgets.png`（苹果表盘式时钟 + 小号数字时间、紧凑页高、无日期卡）
- 截图有桌面透景/BlackScreen 抓取限制；真实桌面材质以用户肉眼为准。

## 仍然保留 / 未做

- 顶部把手仍是**陪伴色**（默认黑），因为它是陪伴球本体；如果用户希望它也跟随卡片材质，另行修改。
- 日期 provider 代码保留，只是不展示/不可添加；以后要恢复只需移除 `TimeOnlyForPresentation` 过滤并恢复菜单项。
- 设置窗口的 ColorPicker 通道输入仍受 240 高限制（上一轮已知）；本轮未动设置。→ **审计修复轮已补齐**：ColorPicker 改为内部 ScrollViewer，通道输入可滚动可达。
- 键盘原生通路（`WM_KEYDOWN`/focus）仍是 native 限制，未改。
- 秒针目前随整页重绘更新（时间 provider 默认每分钟刷新）；如果要做连续 1 秒跳动，需要单独的 1 秒本地重绘 timer；本轮未加，避免额外常驻开销。→ **审计修复轮已改为**结构化的 provider 快照：只有 `ShowSeconds` 时快照才带秒（并按整秒调度刷新），表盘秒针只在快照含秒时出现，不再解析本地化文本。
- 时间卡目前仍保留一个很淡的卡片面；如果你要“手表本体”那种黑底白盘或完全无卡片边界，再单独改。

## 审计修复轮（2026-10-21，依据 `GlassDesk-audit-2026-10-21/AUDIT.md`）

审计结论：1 项 P1 + 11 项 P2 + 2 项既有限制/文档偏差。修复在 `ui/final` 上按 A/B/C/D 批完成，每批独立 commit、0 警告 0 错误、Core tests 全过（最终 **282 checks**）。不涉及 native，不新增导航项、不改设置 IA、不自动删除旧数据（隐藏管理移除是显式且可撤销的操作）。

- **收尾上一轮 WIP**（`48cc7ad`）：Dock 圆角/选中动效、表盘与 provider 快照同步、ColorPicker ScrollViewer、工具栏 44 命中、校验容量映射。
- **A 批**（`5e66f9c`、`e39d0d1`）：P1 `AudioChecks` 依赖闭包修复；空态按钮裁切（最小页高 344）；把手命中区扩为 58×44 且 UIA bounds 一致；无插件音乐页空滚动消除。
- **B 批**（`ced110f`）：捕获所有者优先（拖卡到 Dock 松手不再切页）；禁用控件鼠标/UIA 都不执行；按钮按压态与拖动落点高亮。
- **C 批**（`efddbec`、`6525275`）：时钟结构化数据（修复 fi-FI 分隔符与 12h AM/PM；数字时间右侧 10 DIP 次级墨色 AM/PM，24h 不显示）；添加菜单“已隐藏组件（N）”+ 管理浮层（名称/原因/移除/撤销，容量达 32 时当前页解释并可直接进入管理）。
- **D 批**（`112303b`、`bc63ae4`）：短工作区紧凑布局（优先缩封面与间距，控制行/时间轴/Dock 参与统一布局；360/400/476 DIP 实测按钮完整可见；仅正文滚动、无空滚动）；当前页错误条（保存失败常驻，成功只清对应类，不吞别的错误）。

验收证据：预览构建 `artifacts/qa/ui-audit/staging-f/`；截图在 `artifacts/qa/ui-audit/screens/`（`audit-watch12b/24b`、`audit-h2-*`、`audit-h3-*`、`audit-r3-*`、`audit-n-*`）；未验证项见 `docs/ui-implementation-log-2026-10-19.md` 的审计修复轮小节。
