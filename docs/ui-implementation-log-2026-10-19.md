# GlassDesk UI 实施记录（2026-10-19）

- 计划依据：`docs/implementation-plan-final-2026-10-19.md`（最终施工版）
- 执行分支：`ui/final`（基线 `master@806a9a4`，master 未改动）
- 完成日期：2026-10-19（Asia/Shanghai）
- 状态：**Phase 1–4 已实施；构建 0 警告 0 错误；Core tests 230 checks 全通过；设置页崩溃回归已修复并复测。**

> 截图与构建产物在 `artifacts/qa/ui-final/`（`.gitignore` 已忽略，**未入库**；截图会带出桌面/测试桌面背景，按计划不入库）。

---

## 1. 提交列表（ui/final）

| Commit | 内容 |
|---|---|
| `dff6482` | `ui(phase1): settings cleanup + radius scale` — 设置窗口去噪 + 圆角体系 |
| `5e0926e` | `ui(phase2): top companion handle + bottom dock details chrome` — 详情页结构改造 |
| `a457257` | `ui(phase2): keep plugin tools button clear of dock` — Phase 2 收尾（插件工具按钮上移出 Dock 区） |
| `84e25e1` | `ui(phase1): fix settings page switch crash` — 修复设置页切换崩溃 + 丝带 region 24 + 选中药囊位移 |
| `cf39390` | `merge: phase2 details chrome` — Phase 2 合入 ui/final |
| `59cfd1e` | `merge: phase1 settings crash fix + radius/window shape alignment` |
| `0d2e0d3` | `ui(phase3): widget board + builtin cards visual cleanup` — 小组件板 + 内置卡视觉 |
| `f4795d5` | `ui(phase3): deepen edit card face with 1px accent ring` — 编辑态卡面回修 |

- `git status --short` 为空；`git diff --stat master..ui/final`：20 files, +1435 / −254。
- Phase 1/2 在独立 worktree（`.worktrees/phase1`、`.worktrees/phase2`）并行开发，Phase 3/4 在主工作树；两个 worktree 已合并且不再需要（可 `git worktree remove`）。

---

## 2. 各阶段实际改动

### Phase 1 · 设置窗口去噪 + 圆角体系（合并 5 文件 + 修复 3 文件）
- 新增 `src/GlassDesk.App/UiRadius.cs`：`Shell=24 / Card=16 / Control=10 / Full()`。
- `SettingsWindow.cs`：
  - 卡片 24→16、输入框 24→10、按钮全圆；卡片改 4.5% 墨色浅槽 + 1px 8% 墨边（pane 白填充降为 0）。
  - header 删除“GlassDesk”大标题，只留动态页面名（68→56 高）；右上只留 X（+新增 Esc）；页脚删“关闭设置”；“隐藏组件”36→32 高。
  - InfoBar：默认关，成功用 `Informational` + 2.4s 自动关闭，错误常驻（无绿条）。
  - native 状态 `Disabled` 在 UI 层中文化（显示“未启用”），不改 native。
  - 删除开发期文案；每页 ScrollViewer 尾部 24 DIP，修复“显示星期”与插件列表裁切。
  - ColorPicker：Border r10 + Height 240 + `RectangleGeometry` Clip（SizeChanged 更新）；通道输入被限高裁切的偏差见 §4。
  - 按钮 Normal/PointerOver/Pressed/Disabled 四态；焦点环资源 2px accent + 1px 白。
- `SettingsFloatingNavigation.cs`：外框 24 / 选中底 20、父级 PointerMoved 悬停、选中 accent 12% tint + accent.ink 文字、恢复 `_selection.RenderTransform`（Phase 1 重构遗漏导致胶囊不移动）。
- `SettingsAcrylicBackdrop.cs`：`TintOpacity = 0.86` 固定、Theme 恒 Light，不再跟随“透明度”滑杆（组件玻璃功能保留）。
- `WidgetInstanceConfigurationWindow.cs`：按钮全圆。
- `SettingsWindowShape.cs`（修复阶段授权）：窗形 region 的 pane 与导航圆角同心改为 48 px 直径（r24 DIP），同时缩放安全。

### Phase 2 · 详情页 chrome（合并 13 文件）
- Core 新增 `DetailsChromeLayout.cs`：Home 58×28 top14 r14（44×44 命中）、Dock 320×48 bottom16 r24、5 项中心 32/96/160/224/288（item 64 等分）、选中胶囊 88×36 r18、名字牌 20 高。
- Core `UiPreviewContracts.cs`：`DetailsDockEntry` / `DetailsDockItems`（music/widgets 为真页面；clock/focus/notes 为预览）；`Contracts.cs` 追加 `WidgetActions.DockFirst=120`（旧值未动）。
- Core `WidgetController.cs`：删除 `hasTimeline?512`，音乐详情恒 476。
- App 新增 `DetailsChromeVisual.cs`（671 行）：把手（深色 r14 + 两只眼睛 + 悬停“回首页”白牌 + 44×44 命中 + UIA Invoke）、Dock（像素面图标、白胶囊 180ms 滑动、悬停名字牌、预览项 1.2s 高亮后回真实页、高对比系统色、减少动态效果立即完成；只在状态变化时重绘）。
- `WidgetScene.cs`：`_navigation`→`_chrome` 全量接线；删除旧 99–104/111 路由与右上卫星按钮；精确布局：音乐 360×476（封面 (96,70,168,168) r16、标题 y246、歌手 y274、时间轴 (56,300)、控制 96/152/220、Dock (20,412)）、小组件页 360×512（标题 y62、按钮 y58、卡片 y104、Dock (20,448)）、空态 360×300（viewport offset 56，图标/文案/按钮不裁切）。
- `WidgetScene.Board.cs` / `WidgetBoardRenderer.cs` / `GlassDeskCoordinator.Widgets.cs`：小组件页固定 512、卡片视口 `size.Y-176`、板起点 y104（`WidgetBoardLayout.Top/Gap/BottomInset` 单一来源）。
- `DetailsNavigationVisual.cs` 保留但不再实例化（dead code，可后续删除）。

### Phase 3 · 小组件板 + 内置卡（4 文件）
- `WidgetBoardBuiltIns.cs`：时间/日期卡文字 3–4 层 → 2 层（`04:20` + `UTC+08:00`；`19` + `10月19日 · 周一`）；删除“时间/日期”标签、长时区、今年年份；Medium 卡大值 + 右侧说明；新增 `ZoneCaption`/`DateCaption`/`WeekdayChoice`（DST 用采样时刻计算 offset）。
- `WidgetBoardRenderer.cs`：卡片 r16、Rim .75、卡面/rim 色按计划；工具栏（编辑 ghost 左 + 添加 accent 胶囊右，y=58）；空态垂直居中共 32 浅槽圆；添加菜单右缘对齐、浅色浮层 r12/行高 44；编辑态（加深卡面 + 1px accent 边框 + 右上拖动柄 + 底部 3×44 图标）；卡片按压状态 r16 专属层（去掉通用椭圆暗罩）。
- `WidgetScene.cs`：仅 `UpdateFeedback` 最小改动（按压状态圆角）。
- 测试：内置卡文本/角色/坐标 + 负偏移 UTC 断言（228→230 checks）。

### Phase 4 · 可访问性 / 测试 / 文档 / 收尾
- UIA 实测（自动化枚举，见 §3.4）：详情页 6 个 chrome 控件名称正确；`InvokePattern` 可用（回首页/音乐/小组件/预览项）；设置窗口导航 5 项按钮名正确、页面控件均有 AutomationProperties。
- 键盘 ←/→/Tab：native 窗口 `SW_SHOWNOACTIVATE`、无 WM_KEYDOWN、UIA `IsKeyboardFocusable=false`，本轮**不改 native**，故键盘焦点路径不存在；UIA `Invoke` 为等价可达路径（详见 §4）。
- 文档：更新 `docs/ui-plan-v2-2026-10-19.md` 状态；新增本文档。

---

## 3. 验证证据（2026-10-19）

### 3.1 构建与测试
| 命令 | 结果 |
|---|---|
| `dotnet build src/GlassDesk.App/GlassDesk.App.csproj -c Release`（`-p:GlassDeskNativeDirectory=…/native-ui-preview/0.3.0-preview.2`） | 成功，**0 警告 0 错误** |
| `dotnet build tests/GlassDesk.Core.Tests/… -c Release` | 成功，0/0 |
| `dotnet run --project tests/GlassDesk.Core.Tests/… -c Release --no-restore` | **230 checks passed** |
| `dotnet publish src/GlassDesk.App/… -p:GlassDeskUiPreview=true -o artifacts/qa/ui-final/staging` | 成功（自包含 win-x64，含 native DLL/PRI） |
| 其它检查项目构建：`NativeBridgeChecks` / `PlatformChecks` / `PluginChecks` | 成功，0 警告 0 错误 |
| `AudioChecks` | **master 基线同样编译失败**（该项目用 `Compile Include` 只链接 Core 子集，`Contracts.cs` 引用的 `DetailsPageController` 未链接；与本次改动无关，非回归） |

### 3.2 运行与截图管线
- 以隔离数据目录 `--ui-preview --data-dir=…` 启动；第二实例打开设置；DPI-aware 抓窗口。
- 本机自动化会话存在全屏“BlackScreen”助手窗口，屏幕抓取为黑；改为 **`PrintWindow(PW_RENDERFULLCONTENT)` 抓窗口内容**（透明玻璃为黑色属抓取限制，真实桌面为磨砂玻璃）。
- 截图目录：`artifacts/qa/ui-final/screens/`（未入库）。本轮最终截图（`final-*`，150% DPI，物理像素）：
  - `final-00-capsule-…-324x78.png`：时钟/音乐胶囊
  - `final-01-music-…-540x714.png`：音乐详情（360×476）
  - `final-02-hover-0..4-…-540x714.png`：Dock 5 项悬停名字牌（含“（预览）”）
  - `final-03-widgets-…-540x768.png`：小组件页（360×512）
  - `final-04-edit-mode-…` / `final-05-edit-clean-…`：编辑态（加深卡面 + 1px accent 边 + 拖动柄 + 3×44 按钮）
  - `final-06-normal-…`：退出编辑
  - `final-07-add-menu-…`：添加菜单（右对齐、浅色浮层 r12）
  - `final-08-after-add-time-…`：添加“时间”后 3 卡布局、不压 Dock
  - `final-09-music-again-…`：Dock 切回音乐
  - `final-10-collapsed-…-300x78.png`：点把手回胶囊
  - `final-settings-10-appearance…`、`final-settings-11-category-1..5-…`（交互/陪伴/插件/通用/外观）、`final-settings-12-scrolled-bottom-…`：设置五页 + 滚到底
- 设置页切换回归复测：`插件`/`通用`页点击后 300ms/900ms 截图（旧构建必崩路径），本轮 0 崩溃；WER 统计无新增 `AppCrash_GlassDesk` 记录。

### 3.3 交互实测
- 胶囊→详情→Dock 切页（540×714 ↔ 540×768）→预览项点击不切页（仍 768）→Dock 回音乐→点把手回胶囊（300×78）：通过。
- 小组件页：编辑→完成/取消、添加菜单、添加时间卡（3 卡）、卡片不遮挡 Dock：通过。
- 设置：5 分类切换、悬停与选中态、X/Esc、滚到底无裁切：通过（截图）。

### 3.4 UIA 实测（自动化枚举）
- 详情窗口（540×714）控件：`回首页（陪伴）`（66×66 physical = 44×44 DIP）、`上一首`、`播放/暂停`、`下一首`、`音乐，当前页面`、`小组件`、`时钟（预览）`、`专注（预览）`、`随记（预览）`；6 个 chrome 控件全部 `enabled=True`、bounds 非零，且均支持 `InvokePattern`。
- 实测 Invoke：`小组件` → 页面切换并长高到 768；`时钟（预览）` → 不切页（仍 768）；`回首页（陪伴）` → 收回 300×78 胶囊。
- 设置窗口：导航 5 个 Button 名称（外观/交互/陪伴/插件/通用）+ 关闭/隐藏组件/滑块/开关等均有可访问名称；页面切换后隐藏页控件 `offscreen=True`（正常）。

---

## 4. 与计划的偏差 / 已知限制

1. **键盘 ←/→ + Enter / Tab（未实现，native 限制）**：预览窗口为 `SW_SHOWNOACTIVATE`、native `WndProc` 不处理 `WM_KEYDOWN`、UIA provider `IsKeyboardFocusable=false`，宿主层无键盘输入通路；实现需要改 `native/GlassDesk.Native`（本轮明确不改）。替代：UIA `Invoke` 通道实测可用；鼠标/触控正常。焦点环视觉随自定义焦点资源存在，但因无键盘焦点路径未在实机出现。
2. **设置 ColorPicker 精确输入被限高裁切**：按计划“Border r10 + 圆角 Clip + 高度约 240”实现；色盘/预览可用，通道滑杆与十六进制输入在 240 高度下不可见（需滚动/放宽高度才能完整暴露）。未删除功能，但需用户确认是否接受；建议后续把 ColorPicker 放进 ScrollViewer 或改用设计稿的色板行。
3. **详情页音乐卡不再显示 8 根频谱**：新布局（时间轴 y300 + 控制 y340–396）无频谱位置，计划 §3.1 元素表也未列；频谱与液体动画在胶囊/Peek 完整保留（`_bands`/`UpdateAudio` 未动）。
4. **默认小组件板只有 2 张小卡**：`WidgetBoardData.CreateDefault` 仍只创建时间 + 日期两卡（未改用户数据默认值）；设计图的第三张宽卡（时间·日期组合）需用户“添加”或未来新增组合 provider；中卡（312×148）布局与视觉已支持并实测添加成功。
5. **预览项点击行为**：白胶囊滑到预览项高亮 1.2s 后回真实页面，不切页、不做假页面（计划允许“轻提示或仅高亮”，取仅高亮）。
6. **Dock/把手阴影**：未加 `0 6 18 rgba(0,0,0,.18)` 阴影（计划标注“可用”，可选），采用实心深色圆角面，避免新增材质基础设施。
7. **`WidgetBoardLayout.Top` 132→104 + 新增 `BottomInset=72`**（Core `WidgetBoardData.cs`，计划 §5 允许“如需”）：让壳、renderer、coordinator、测试共用单一版式来源；顺带修复 Phase 2 遗留的滚动 extent 少算 48 DIP 问题。
8. **日期卡默认周格式 short**：`DateWidgetSettings.WeekdayFormat` 默认 `long`→`short`，使默认卡显示与计划示例一致的“周一”；用户选“完整星期”仍显示“星期一”，已保存的旧设置不受影响。
9. **12 小时制/带秒的大值字号回退**：Small 卡在这些格式下用 20 DIP 标题角色（`WidgetClock` 36 DIP 会裁切），12 小时制默认格式仍为 36 DIP 大值。Medium 卡大值 + 右侧说明。
10. **详情页插件工具按钮**：从 `(16, height-48)` 上移到 `(16, height-104)`（Dock 上方 8 DIP 空隙），避免压 Dock（计划未给坐标，属最小修正）。
11. **声音/时间轴相关**：本机媒体会话（网易云音乐）不提供 Timeline，音乐页不显示进度条/时间标签；计划中的 `(56,300)` 时间轴区域与 Seek 命中仍按计划实现（`SeekAvailable` 时启用）。

---

## 5. 未验证项（环境限制）

- **空态 360×300 截图**：本机 Windows 媒体会话始终存在（网易云音乐），应用永远处于“已连接”状态，无法在实机截到空态；空态布局已按代码坐标 + 计划调整（viewport offset 56、内容 62/118/162），但未截图确认。
- **音乐页时间轴/进度条**：同上，当前媒体无 Timeline；`Seek`/时间轴绘制未实机看到。
- **100% / 200% DPI、混合 DPI 多显示器**：仅验证 150% 单屏（2560×1600，截图物理尺寸 540/714/768/1320×1020 符合预期）。
- **高对比模式实机配色、减少动态效果实机表现**：代码路径已按系统色/立即完成实现，未在实际系统开关下截图。
- **10 英尺/触摸、真实网易云客户端交互、安装器、驻留/开机自启**：未回归（本计划范围外）。
- **键盘可达性**：见 §4.1（native 限制）。

---

## 6. 回滚

```powershell
# 整阶段/单个提交
git revert <commit>
# 整个 UI 分支（回到基线）
git checkout master
```

产物目录 `artifacts/qa/ui-final/`（staging / screens / data-*）未入库，可随时删除。

## 7. 后续建议
1. 若需要键盘可达：单独开 native 任务，在 `GlassDesk.Native` 增加 `WM_KEYDOWN`/焦点处理与 UIA `IsKeyboardFocusable`，再补 Tab 顺序与焦点环。
2. ColorPicker 若要完整功能：改为 ScrollViewer 包裹或按设计稿换色板行（6 色 + 自定义）。
3. 默认板若要与设计图一致：给 `WidgetBoardData.CreateDefault` 增加一张 Medium 时间卡，或新增“时间·日期”组合 provider（需产品确认）。
4. 删除已停用的 `DetailsNavigationVisual.cs`（dead code）与旧 `NavigationInteractionModel` 引用。
