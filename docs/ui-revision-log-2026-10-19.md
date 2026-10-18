# UI 修订记录（2026-10-19，第二轮反馈）

依据：用户对 Phase 1–4 实际截图的反馈。只改细节，不动已确认的结构。

## 反馈 → 处理

| 用户反馈 | 处理 |
|---|---|
| 图一：“回首页”只需要点击陪伴球，不要其他东西 | 删除顶部悬浮的“回首页”文字牌；首页把手保留，点击仍回胶囊。UIA 名称保留（不可见） |
| 图一：底部导航没做好，颜色统一，不要黑色 | Dock 从 `#202328` 实心黑条改为**自适应半透明材质**：深底用白色薄膜 + 白色图标，浅底用墨色薄膜 + 墨色图标；不再有黑色底 |
| 图一：不要文字，直接表示 | Dock 去掉选中项文字、悬停名字牌；只保留图标 + 一个 36×36 选中圆。顶部把手也去掉文字 |
| 图二：不要这么多文字 | 小组件页删除“我的组件”标题；添加/编辑改为**纯图标按钮**（+ / 铅笔）；卡片不再显示“时间/日期”标签和 UTC 长说明 |
| 图二/图三：时间小组件改成图 3 样式，其他小组件暂时不要 | 初版误把“图 3”当成指针钟设计稿；收到“只是表示一下”后已撤回指针钟，改为**纯数字时间卡**（只显示 `04:52`，无标签/无时区）；日期卡从默认板、添加菜单和显示层中移除；旧 date 数据保留但不显示 |

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
  - 时间卡只画一个大号数字时间（无标签、无时区、无指针）；编辑态仍显示卡片面；
  - 其他 provider（如旧日期卡）仍走原通用绘制逻辑。
- 不涉及：native、设置窗口、插件、正式配置。

## 验证

- `dotnet build src/GlassDesk.App/... -c Release`：成功，0 警告 0 错误。
- `dotnet run tests/GlassDesk.Core.Tests ... -c Release --no-restore`：**230 checks passed**。
- 预览启动 + 真实指针点击 + 窗口截图（150% DPI）：
  - `artifacts/qa/ui-final3/screens/1-capsule-324x78.png`
  - `artifacts/qa/ui-final3/screens/2-music-540x714.png`（Dock 无文字、无黑条）
  - `artifacts/qa/ui-final4/screens/final-widgets.png`（纯数字时间卡、紧凑页高、无日期卡）
- 截图有桌面透景/BlackScreen 抓取限制；真实桌面材质以用户肉眼为准。

## 仍然保留 / 未做

- 顶部把手仍是**陪伴色**（默认黑），因为它是陪伴球本体；如果用户希望它也跟随卡片材质，另行修改。
- 日期 provider 代码保留，只是不展示/不可添加；以后要恢复只需移除 `TimeOnlyForPresentation` 过滤并恢复菜单项。
- 设置窗口的 ColorPicker 通道输入仍受 240 高限制（上一轮已知）；本轮未动设置。
- 键盘原生通路（`WM_KEYDOWN`/focus）仍是 native 限制，未改。
- 时间卡目前是纯数字（24 小时制来自时间 provider）；如果之后要 12 小时制数字显示或圆形/胶囊形态，再单独定。
