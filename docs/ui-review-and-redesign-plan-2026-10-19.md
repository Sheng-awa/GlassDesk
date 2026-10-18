# GlassDesk UI 审查与改版计划（Quiet Glass）

> **已被 2026-10-19 的简化版取代：[ui-plan-v2-2026-10-19.md](ui-plan-v2-2026-10-19.md)。**
> 本文件保留作为详细证据/缺陷附录；执行以 v2 为准（v2 不重排结构、不减功能）。

整理日期：2026-10-19（Asia/Shanghai）
状态：**仅规划，等待用户批准。本文档不构成改码授权。**
前提：源码已在 `共同工作区\GlassDesk` 建立 Git 保护（见第 1 节）。

---

## 0. 结论摘要

1. 现有实现的功能骨架和排版 token 已经比 0.2.0 明显进步，但视觉上仍显得“平、灰、散”：**设置窗口把 4 层半透明白叠在一起**，主组件玻璃在浅背景下缺少边界，层级主要靠留白而不是靠明度与字重。
2. 用户审美目标是 Google（Material 3）+ macOS（HIG）的极简。这两套体系的共同点是：**一层材质 + 清晰的内容面 + 单一强调色 + 明确的主操作 + 克制的动效**。当前实现四点都不足。
3. 最扎眼的缺陷不是“不够花”，而是**该有的状态没有**：全窗按钮没有 hover/pressed；设置导航的 hover 资源永远不会触发；选中态在亮背景几乎不可见；成功提示是常驻绿色横幅；关闭入口有两个；页面标题重复。
4. 建议方向命名为 **Quiet Glass（安静玻璃）**：保留玻璃、保留五槽陪伴导航、保留几何与蓝色 accent；把设置改成“单列分组列表”，把主组件改成“少而准的层级 + 明确的播放主按钮 + 常驻页面语义”，把小组件板从“4% 隐形卡”改成“轻纸面卡”。
5. 本轮改动分 5 个批次，**第 1 批不触及 P1 保护项**（先修状态与 token）；第 2-4 批会触及设置布局、播放控件权重、五槽语义，需要你逐项批准。无 computer use，实机截图/交互验收需要你本地跑预览或提供截图（见第 8 节）。

---

## 1. 前置：Git 保护与验证边界

### 1.1 Git 保护（已完成）

| 项目 | 状态 |
|---|---|
| 仓库位置 | `共同工作区\GlassDesk\.git` |
| 基线提交 | `9a9ccf8 baseline: import complete source snapshot before UI review (2026-10-19)` |
| 受跟踪文件 | 基线提交 272 个（97 `.cs`、33 `.md`、17 `.csproj`、11 `.cpp` 等）；本计划与样张随后以独立 commit 入库 |
| 忽略 | `artifacts/`、`**/bin/`、`**/obj/`、`.tools/`、`.vs/`、`*.user` |
| 配置 | `core.autocrlf=false`、`core.longpaths=true` |
| 当前工作树 | 干净（`git status` 为空） |

建议：改版在 `ui/quiet-glass` 分支进行，每个批次一个 commit；`artifacts/` 不入库，构建产物不入库。回滚 = `git checkout 9a9ccf8 -- <file>`。

### 1.2 可验证 / 不可验证边界（如实声明）

| 可做 | 不可做（按你的说明跳过） |
|---|---|
| `dotnet build`（已验证：Core 测试工程 3 秒编译通过，SDK 10.0.401 与 global.json 一致） | 实机 GUI 截图、鼠标/键盘交互（本会话无 computer use） |
| Core / Platform / NativeBridge / Plugin 单元检查 | 混合 DPI、多屏、真实 100%/200% |
| 静态对比度计算（WCAG 公式，基于最终 token 合成色） | Present 帧率、连续驻留、资源占用 |
| 发布到新 staging（显式 ABI2 原生目录） | 真实网易云连接、安装/升级/卸载 |
| HTML 视觉样张（浏览器打开，供你验收方向） | 桌面合成效果、液态玻璃实时表现 |

因此：**代码、构建、静态可读性由我保证；最终“看起来对不对”必须由你本地打开预览或看截图确认。** 我不会把“编译通过”写成“视觉验收通过”。

---

## 2. 审查证据

### 2.1 截图证据（均为历史批次，非本轮新拍）

| 证据 | 路径（相对项目根） | 说明 |
|---|---|---|
| 设置窗口（真实桌面，陪伴页） | `artifacts/qa/typography-widgets-preview2/p2-settings-companion-desktop.png` | 玻璃 + 白卡 + 顶部浮动导航的真实合成效果 |
| 设置窗口（通用页） | `artifacts/qa/companion-liquid-review/p2-settings-general-current.png` | 绿色 InfoBar、重复标题、裸行、页脚 |
| 设置窗口（插件页） | `artifacts/qa/companion-liquid-review/p2-settings-plugins-current.png` | “Disabled” 英文、列表被视口裁切 |
| 设置窗口（交互页） | `artifacts/qa/companion-liquid-review/p2-settings-interaction.png` | 两列失衡、大量空白 |
| 设置窗口（外观页） | `artifacts/qa/companion-liquid-review/opacity-fixed-appearance.png` | 滑块/开关蓝色状态、卡片网格不等高、1px 滚动条 |
| 设置窗口（陪伴页，窗口单独截图） | `artifacts/qa/companion-liquid-review/p2-settings-companion-revisit.png` | ColorPicker 占满整卡、下半屏空白 |
| 设置窄窗 | `artifacts/qa/companion-liquid-review/r3-settings-narrow.png` | 560×600 DIP 下的压缩表现 |
| 12 格状态矩阵 | `artifacts/qa/typography-widgets-preview2/p2-clock-static-matrix.png` | 胶囊/小展开/大展开 × 深/浅/花/灰背景 |
| 胶囊裁切 | `artifacts/qa/typography-widgets-preview2/matrix-valid-light-capsule-crop.png` | 白胶囊 + 黑球 + 时间 |
| 小展开裁切 | `artifacts/qa/typography-widgets-preview2/matrix-valid-light-peek-crop.png` | 时间、连接文字、陪伴贴边（旧批次，裁切已另修） |
| 大展开裁切 | `artifacts/qa/typography-widgets-preview2/matrix-valid-light-details-crop.png` | 黑胶囊导航、空态、稀疏布局 |
| 液态玻璃大展开 | `artifacts/qa/ui-0.3.0-preview.1/resume-glass-light-details-crop.png` | 封面/标题/三圆控制 + **背景文字透过卡片可读** |
| 时钟小展开修复 | `artifacts/qa/typography-widgets-preview2/clock-fix-peek-stable-desktop.png` | 陪伴完整可见；小展开内容重心偏左 |
| 小组件板 | **无截图** | 候选构建成功但从未启动（手册 §6.3、§7） |

> **截图与当前源码不同版本（重要）**：上列 `matrix-valid-*`、`resume-glass-light-details-crop` 等大展开截图来自 0.3.0-preview.1 及更早布局（320×440 / 280×232）。当前源码已改为 **360×300 / 360×476**（`ContentPolicy.cs:39-44`、`WidgetController.cs:44`），封面 192、页名、时间轴、卫星都是之后才加的，**没有当前布局的截图**。因此缺陷清单里凡涉及大展开比例与间距的判断，都以代码坐标为准；像素观感需在当前候选上重新截图确认。

### 2.2 代码证据

| 文件 | 关键事实 |
|---|---|
| `src/GlassDesk.App/SettingsWindow.cs` | 窗口 880×680 DIP；白 alpha 常量 `0x50/0x45/0x78/0x86FFFFFF`；卡片、按钮、InfoBar、ColorPicker 全在 22-120 与 1043-1170 行；`ButtonBackground/PointerOver/Pressed` 三态同色 |
| `src/GlassDesk.App/SettingsFloatingNavigation.cs` | 导航 56 高 / MaxWidth 480 / 圆角 28；选中白胶囊 `0xD9FFFFFF`、底板 `0x52FFFFFF`；`buttonGrid.IsHitTestVisible=false` 使 hover 资源永不触发；键盘 ←/→/Home/End 可用 |
| `src/GlassDesk.App/SettingsAcrylicBackdrop.cs` | 白 tint + `TintOpacity=用户透明度滑块` + Light 主题；设置窗口材质随桌面变化 |
| `src/GlassDesk.App/SettingsWindowShape.cs` | `SetWindowRgn` 两个圆角矩形；硬边、无 DWM 阴影 |
| `src/GlassDesk.App/SettingsColumns.cs` | 2 列阈值 `width >= 300*2+12`；按行取最大高度 → 左右列底边不齐 |
| `src/GlassDesk.App/SettingsTypography.cs` + `src/GlassDesk.Core/TypographyCatalog.cs` | 已定义 TextRole/InkRole（Primary #20252C / Secondary #48515B / Caption #626C78 / Disabled #8E98A3）；CJK 600 实际回退 400-500 |
| `src/GlassDesk.App/DetailsNavigationVisual.cs` | 五槽 = 5 根圆角竖条 + 48×48 眼睛面；导航下方 y=76 已有 264×20 页面名文字；卫星 28 可见 + 44 命中，选中环 `#1689F9` |
| `src/GlassDesk.App/WidgetBoardRenderer.cs` | 卡片 tone `0x0AFFFFFF`（4% 白）+ rim 0.65 DIP；工具栏是纯文字“添加/编辑”；编辑态小卡内 2×2 四个 44 高按钮；空态两行文字 |
| `src/GlassDesk.Core/WidgetBoardData.cs` | 左 24、间距 12、小卡 150×148、中卡 312×148、卡片顶 132；双列阈值 `availableWidth ≥ 312+48`；详情页与小组件页同宽 360 |
| `src/GlassDesk.App/WidgetScene.cs` | 尺寸以 `src/GlassDesk.Core/ContentPolicy.cs:39-44` 为准：胶囊 200×52（音乐胶囊 208-296×52）、时钟小展开 240×144、音乐小展开 320×176、大展开 360×300（未连接）/ 360×476（播放、时间轴无效）/ 512（时间轴有效）；大展开已连播：上一首(96,y+6,44)/播放(152,y,56)/下一首(220,y+6,44)，封面 192@(84,88)，时间轴 248@(56,min(382,H-130))，导航 264×48@(48,16)，页名 caption 264×20@y76（与封面 y88 有 8 DIP 重叠风险） |
| `native/GlassDesk.Native/GlassDeskNative.cpp` | 原生文字/材质绘制；rim、tonal 层、简化材质路径在此（改动需重建 ABI2 DLL） |

---

## 3. 缺陷清单

分级：**P0** = 直接破坏观感或可用性，必须先修；**P1** = 破坏一致性/信息层级；**P2** = 打磨项。
“触保护”列：`是` = 改动会触及 P1 保护项或既有用户决定，需要你批准；`—` = 可直接修。

### 3.1 A 组：材质与层次（全局）

| ID | 级别 | 缺陷 | 证据 | 触保护 | 修复方向 |
|---|---|---|---|---|---|
| A1 | P0 | 设置窗口 4 层半透明白叠加（acrylic→pane 0x50→卡 0x45→按钮 0x86），整体发灰、层级糊 | SettingsWindow.cs:22-27,266-270,1110-1113,1043-1064；桌面截图 | 是（玻璃层数） | 固定一层材质；分组面改用 4.5% 墨色浅槽，不再叠白 |
| A2 | P0 | 设置材质透明度跟随“组件透明度”滑块，深色桌面上设置窗口变灰、正文对比下降 | SettingsAcrylicBackdrop.cs:32-36 | 是 | 设置窗口独立固定材质（白 tint .86-.90，Light） |
| A3 | P1 | 窗口圆角用 `SetWindowRgn`，硬边无抗锯齿、无 DWM 阴影 | SettingsWindowShape.cs:16-23 | 是（窗口形） | Phase 2 spike：DWM 圆角/透明边 + 1px 内高光；不行则加外侧 rim 与柔和阴影 |
| A4 | P0 | 液态玻璃中心过透，浅色背景下背景文字透过卡片直接可读，抢正文（旧布局截图，但问题在材质层，与尺寸无关） | `resume-glass-light-details-crop.png` 中 “QA: ordinary captioned window…” 清晰可读 | 是（材质） | 文字区 4-6% tonal scrim + 提高扩散模糊；仍保留真实背景透景 |
| A5 | P1 | rim/阴影无统一规则：板卡有 0.65 rim，设置无 rim，主外壳 rim 在 native，按钮无 elevation | WidgetBoardRenderer.cs:30,263-288；native | — | 统一 token：rim 0.75、左上光向、0 8 24 阴影 |
| A6 | P1 | 三套蓝不一致：设置/开关用 #1689F9；主组件内部 accent 仍 #5D6F84；卫星选中环 #1689F9。且 #1689F9 作为小字/白字对比仅约 3.55:1，不满足 4.5:1 | TypographyCatalog 与截图；WCAG 计算 | — | 双角色：`accent.fill` #1689F9（填充/图标/≥3:1 组件）与 `accent.ink` #0B6BCB（小字/链接 4.5:1）；主组件 accent 统一到同一族 |
| A7 | P1 | 液态玻璃 shader 自带 veil+rim（`native/…/LiquidGlass.cpp:106-115`），原生再叠 tint+border（`GlassDeskNative.cpp:407-413`），可能“双层纱/双边光”；当前无开/关对照截图 | 代码 | — | Phase 3 先拍液态开/关两态对照，再合并为一层 rim；不碰采集管线 |

### 3.2 B 组：设置窗口

| ID | 级别 | 缺陷 | 证据 | 触保护 | 修复方向 |
|---|---|---|---|---|---|
| B1 | P0 | 标题重复：header“GlassDesk” + 内容 H1“外观/插件/…” | S:1087 与 357/370；通用页截图 | 是（布局） | 合并为单一页面标题；header 只留窗口级操作 |
| B2 | P0 | 常驻绿色成功 InfoBar 独占内容顶部，带关闭 X；“已更新 位置已重置。”两条消息拼接 | S:355,1067-1073；通用页截图 | 是 | 中性瞬时 toast（1.6-2.4s 自动消失），仅错误常驻 |
| B3 | P0 | 关闭入口重复：右上 X + 页脚“关闭设置”；无 Esc | S:304-310,541-547 | 是 | 只保留 X + Esc；页脚左留“更改会自动保存” |
| B4 | P1 | “隐藏组件”胶囊 36 高与 X 40 圆按钮形状/权重不统一 | S:292-303 | 是 | 改为 32 图标按钮（eye-slash）+ tooltip，或统一 ghost 文本按钮 |
| B5 | P0 | 2 列卡片网格左右高度不齐、底部空白大；陪伴页下半屏空 | SettingsColumns.cs:19-37；外观/陪伴截图 | 是（列布局） | 单列分组列表（推荐），或保留两列但等高分组 |
| B6 | P1 | 控件密度失衡：ColorPicker 占满整卡；插件列表被视口裁切；页脚压内容 | S:56-61,449；D:22-26；插件/陪伴截图 | 是（控件排列） | 紧凑色板 + 自定义浮层；列表底部 24 padding |
| B7 | P1 | Slider/Toggle/ComboBox/TextBox/ColorPicker 全是 WinUI 默认模板 | S:1043-1064 | — | 自定义统一模板（见 5.2） |
| B8 | **P0** | **全窗按钮无 hover/pressed**：三态资源同色 0x86FFFFFF | S:1048-1049 | — | 分开 Normal/Hover/Pressed/Disabled，120ms 过渡 |
| B9 | **P0** | **导航选中态在亮背景几乎不可见；无 hover**：选中 0xD9 与底板 0x52 差 alpha；`IsHitTestVisible=false` 吃掉 hover；CJK 无字重区分 | N:46-49,54,68-69；截图 | 是（顶部导航） | accent 12% 选中底 + accent.ink 文字 + 有效 hover；键盘焦点环 |
| B10 | P1 | 页脚“更改会自动保存”与实际反馈不一致：没有保存状态、关窗即关 | S:541-547 | — | 保留 caption；保存失败才出现错误条 |
| B11 | P0 | 英文/枚举直出：“Disabled”“AudioStatus” | S:869-870 → LiquidGlass.cpp:224；GlassDeskCoordinator.cs:68 | — | 本地化映射（已停用/不可用/失败原因） |
| B12 | P1 | 开发期文案出现在用户界面（“UI 预览不修改系统启动和全局快捷键。”） | 通用页截图 | — | 收进“关于/诊断”折叠区 |
| B13 | P1 | ColorPicker 无预设色、无当前色值，视觉像开发工具 | S:56-61；陪伴截图 | 是 | 8 个预设色 + “自定义”浮层（小号 HSV/hex） |
| B14 | P1 | 形式选择用 ComboBox，选项少且无法即时看效果 | S:62；陪伴截图 | 是 | 3 张形式卡（原生/猫耳/恶魔），带迷你轮廓 |
| B15 | P2 | “恢复默认”“导入本地插件”是裸文字，无按钮语义 | S:364,415 | — | secondary 按钮 |
| B16 | P2 | 滚动条是 1px 灰线（默认 ScrollViewer） | 外观/插件截图 | — | 4px 圆角、悬停加深、自动隐藏 |
| B17 | P1 | 窄窗 560×600 下顶部导航 5 等分压缩文字，无折叠 | N:59；`r3-settings-narrow.png` | 是（导航） | 窄窗缩短标签或切图标（需批准） |
| B18 | P2 | 窗口 880×680 内容稀疏，缺“紧凑感” | S:1081-1083 | 是（布局） | 收窄到约 720×680（只收宽度，不改高度与滚动结构） |

### 3.3 C 组：主组件（胶囊 / 小展开 / 大展开 / 导航）

| ID | 级别 | 缺陷 | 证据 | 触保护 | 修复方向 |
|---|---|---|---|---|---|
| C1 | P0 | 五槽导航不可发现：5 根 w7-11/h14-29/r4 白条，无图标无文字；**中心槽在 distance=0 时 opacity=0**（选中槽本身不可见），选中没有环/下划线，唯一强指示是小组件页卫星的蓝环；4 个预览槽看起来像可用 | DetailsNavigationVisual.cs:53,151,153-156,173-188；大展开裁切图 | 是（五槽几何/语义） | **不改几何**：保留“导航下页名 + hover 名称 + 卫星环”；把页名 caption 从 y76 移到 68（避免压封面）；预览槽点击只提示“预览，音乐页保持”；不新增图标/槽位 |
| C1b | P0 | 页名 caption 264×20 @y76 与封面 y88 重叠 8 DIP（封面 88-280） | DetailsNavigationVisual.cs:188；WidgetScene.cs:740；源码坐标计算 | 是（新增语义层位置） | caption 改 y68、12/16 行高（68-84），封面保持 88；音乐页与小组件页共用 |
| C2 | P1 | 大展开三圆等权：播放 56、前后 44，全部同灰同材质，主操作不突出 | WidgetScene.cs:1148-1150（播放 152,y,56 / 前后 96/220,y+6,44）；旧版截图 | 是（播放按钮） | 播放改实心墨色主按钮，前后改幽灵图标；保持中心轴（预览 x=96..264）与几何 |
| C3 | P1 | 大展开空态稀疏：44 图标（body y26→绝对 y106）+ 两行文字（绝对 y162/y231），连接动作像“说明”不像“按钮” | WidgetScene.cs:1154,1163-1165；360×300 空态 | — | 图标 32、等待音乐 16/24、连接 secondary 按钮；保持无底板、无设置按钮 |
| C4 | P1 | 时钟小展开“打开网易云音乐”是 24 高裸文字，无按钮语义 | WidgetScene.cs:1134；clock-fix 桌面图 | 是（小展开布局） | 改为 24-28 高 ghost 文本按钮（带 chevron/箭头），时间板不动 |
| C5 | P1 | 时钟小展开内容重心偏左（陪伴贴边后时间与动作用中轴，视觉失衡）；日期/星期开启后的位置未在截图确认 | clock-fix-peek-stable-desktop.png；DrawPeek:1129-1134 | 是 | 时间与连接动作统一中轴；日期行作为 12/16 caption；保留随机陪伴落点 |
| C6 | P0 | 浅背景下白卡与桌面几乎无边界；深/花背景下玻璃透出色块，正文对比边缘（复杂背景 4.518:1） | matrix 12 格；手册 §7.1 | 是（材质） | rim + 4-6% scrim + 提高模糊；墨色按 backdrop 自适应（P2 已有策略） |
| C7 | P1 | 陪伴球在浅卡上是近黑实心球 32 DIP，眼睛小，辨识度一般 | 胶囊裁切图 | 是（陪伴身体） | **不重画**：仅加 0.75 外描边/内高光；颜色仍由用户设置 |
| C8 | P2 | 播放/暂停按压 94%、260ms 已实现，但三圆缺 hover 反馈 | ui-acceptance 记录 | — | 加 hover 墨色 8% 变化；不动几何 |
| C9 | P1 | 播放态纵向节奏过紧：标题 296-320、歌手 324-344、时间轴 346 起（仅 2 DIP 间隔）；旧截图“下半部空”的问题在当前布局已变为“信息挤在一起” | WidgetScene.cs:1197-1199,617；代码坐标计算 | 是（布局） | 标题/歌手各上移 6（290/318），时间轴 342-382；控制区保持 398/434，底部留 16 DIP |
| C10 | P1 | 保护项缺口：白板填充 alpha 在 TintOpacity≥36%（透明度≤64%）时 ≤0，直接不画 | WidgetScene.cs:1413-1414 | 是（白板策略） | 记录为已知缺口；修复需你授权，且不得改成实心回退 |

### 3.4 D 组：小组件板（从未实机验收）

| ID | 级别 | 缺陷 | 证据 | 触保护 | 修复方向 |
|---|---|---|---|---|---|
| D1 | P0 | 卡片 4% tonal + 0.65 rim，亮背景几乎不可见，缺少“可拖动卡片”的实体感 | WidgetBoardRenderer.cs:56,263-288 | 是（新页面方向） | 轻纸面：白 10-12%（浅）/墨 6%（深）+ 1px rim + 轻阴影 |
| D2 | P1 | 编辑态小卡 150×148 内塞 2×2 四个 44 高按钮，拥挤 | :94-114 | 是 | 编辑态改为底部 3 图标行 + 右上拖动柄，或“…”菜单 |
| D2b | P1 | 添加菜单 152×100 位于 `x=right-205`，与“添加”按钮（宽≤60，右侧 208-268）不对齐 | WidgetBoardRenderer.cs:60-76,201-215 | — | 菜单右缘对齐“添加”按钮右缘；改为 8 圆角浅色浮层 |
| D3 | P1 | 工具栏“我的组件 + 添加/编辑”是纯文字，无按钮视觉 | :64-76 | 是 | “添加”accent 胶囊 + “编辑”ghost；标题 16/24 semibold |
| D4 | P1 | 空态只有左对齐两行文字（24,24 / 24,62） | :194-198 | — | 图标 + 标题 + 说明 + 主按钮，垂直居中 |
| D5 | P1 | 卡片/按钮无 hover，编辑完成/取消无主次 | 代码 | — | 统一状态体系 |
| D5b | P1 | 板卡没有专属按压状态层：通用反馈按 `min(w,h)/2` 画圆角，整卡会变成“近椭圆暗罩” | WidgetScene.cs:1077-1082 | — | 板卡用 r20 专属状态层；只在卡片内缘绘制 |
| D6 | P0 | 本页面没有任何截图/实机证据 | 手册 §6.3 | — | Phase 4 首次启动候选并截图验收 |
| D7 | P2 | 音乐↔小组件切页无 160-220ms 过渡（P3 待办） | 手册 §7.2 | — | 补页面过渡（与外壳形变复用时间轴） |

### 3.5 E 组：排版 / 色彩 / 可访问性

| ID | 级别 | 缺陷 | 证据 | 触保护 | 修复方向 |
|---|---|---|---|---|---|
| E1 | P1 | 设置页字号混用 13/14/16，未按 token 角色使用 | SettingsWindow.cs:1137-1150；TypographyCatalog.cs:42-47 | — | 按 5.2 角色表统一 |
| E2 | P1 | CJK 无真实 600，选中/强调只用 Latin 字重 → 中文层级弱 | TypographyCatalog.cs:42-49,167 | — | 用字号/颜色/底色表达层级，不靠伪粗体 |
| E3 | P1 | 墨色 4 档已定义但设置页只用 2 色；主组件 Caption 被强制 Secondary（WidgetScene.cs:1428），板也只用 Primary/Secondary | 截图 + token + 代码 | — | 全面套用 InkRole（caption 用于单位/次要说明，不再当正文） |
| E8 | P2 | 多处硬编码色未归一：封面 #E6EAED/#30373D/#70918C、时间轴 #15CAD1D8/#F6F8FA、频谱 #596775、hover #303A45 | WidgetScene.cs:1330-1339,601-608,1397；WidgetBoardRenderer.cs:204 | — | 归入 token；频谱颜色属于保护项，只归一非保护区 |
| E4 | P0 | 已知识别失败：浅背景陈旧墨色 1.180:1 / 1.027:1 | 手册 §7.1 | 是（P2 策略） | scrim/rim + 墨色档位 + 去抖取样范围；不引入持续回读 |
| E5 | P1 | 键盘焦点无可见环（导航按钮 Focus 后无视觉） | N:65-66,80 | — | 2px accent ring + 1px 白色内间隔，仅键盘显示 |
| E6 | P2 | 命中尺寸不统一：标题栏 36/40，列表按钮 44 混用 | S:297-310 | — | 视觉可小，命中统一 ≥44 |
| E7 | P2 | 高对比映射只做了部分（Brush()），SurfaceOp/主外壳未系统化 | N:261-270 | — | 系统化高对比 fallback 并列出未测项 |

### 3.6 F 组：文案与本地化

| ID | 级别 | 缺陷 | 证据 | 修复方向 |
|---|---|---|---|---|
| F1 | P0 | “Disabled”、枚举名直出 | S:869-870 | 本地化映射 |
| F2 | P1 | “已更新 位置已重置。”两条消息拼接 | InfoBar 截图 | 一次一条，动作完成即写清 |
| F3 | P1 | “打开网易云音乐”在胶囊/小展开/大展开重复且有时不可点 | WidgetScene.cs:1134,1154,1281 | 统一为“连接网易云音乐”（未连接）/“打开网易云音乐”（已连接） |
| F4 | P1 | 猫耳/恶魔说明长且重复出现 | 陪伴页截图 | 一行 caption，详情放 tooltip |
| F5 | P2 | “UI 预览…”“诊断路径”等开发信息占用主界面 | 通用页截图 | 收进“关于/诊断” |

---

## 4. 改版方向：Quiet Glass

### 4.1 六条原则

1. **一层材质**：窗口/浮窗只有一层玻璃；内容面只允许一种“浅槽”（4.5% 墨色）或“轻纸面”（卡片），不再白叠白。
2. **用空间和字重分层，不用装饰分层**：分区靠 8pt 间距、1px 分隔线、字号/字重/墨色三档；不用彩色边框、渐变、发光。
3. **主操作必须唯一且明显**：accent 或实心墨色；一切可点击元素有 hover→press 状态；不可用项有禁用外观。
4. **克制用色**：中性灰阶 + 单一 accent 蓝；成功/错误色只出现在瞬时反馈；不出现大面积状态色横幅。
5. **信息完整但安静**：每个界面回答“这是什么 / 现在什么状态 / 我能做什么”；删除开发文案、重复标题、重复关闭入口。
6. **动效服务状态**：hover 120ms、状态 180ms、页面 220ms，统一 `cubic-bezier(0.2,0,0,1)`；不做弹跳与无限循环；尊重减少动态效果。

### 4.2 设计 token（建议值）

**颜色（浅色主题）**

| Token | 值 | 用途 |
|---|---|---|
| `ink.primary` | `#20252C` | 标题、主值 |
| `ink.secondary` | `#48515B` | 正文、说明 |
| `ink.caption` | `#626C78` | 辅助说明、单位 |
| `ink.disabled` | `#8E98A3` | 禁用（不承载唯一信息） |
| `accent.fill` | `#1689F9` | 填充、滑块轨道、开关 on、图标（组件对比 ≥3:1） |
| `accent.ink` | `#0B6BCB` | 小字/链接（对白底 ≥4.5:1） |
| `accent.tint` | `rgba(22,137,249,0.12)` | 选中底、tint 按钮 |
| `surface.group` | `rgba(32,37,44,0.035)` | 设置分组浅槽 |
| `separator` | `rgba(32,37,44,0.10)` | 分隔线 |
| `success / error` | `#1E8E3E / #D93025` | 仅瞬时反馈 |

深色材质沿用现有 `InkRole` 的 light-ink 四档（#F8FAFC / #DFE5EC / #B4C0CC / #84909C），本轮不新增深色主题。

**字体（设置窗口角色，全部走现有 Segoe UI Variable；不装字体）**

| 角色 | 字号/行高 | 字重 | 用途 |
|---|---|---|---|
| page.title | 20/28 | 600（CJK 500） | 页面标题 |
| section.title | 13/18 | 600（CJK 500） | 分组标题（secondary 色） |
| label | 14/20 | 400 | 行标题 |
| value | 14/20 | 500，tabular | 滑块数值 |
| description | 12/16 | 400 | 行说明 |
| nav | 13/18 | 400/500 | 顶部分类 |
| button | 13/18 | 500 | 按钮 |

主组件继续使用 `TypographyCatalog` 现有角色（clock.capsule 25、clock.peekHero 34、media.detailsTitle 20 等），**不改字号**。

**形状与间距**

| Token | 值 |
|---|---|
| radius.window / pane | 24 |
| radius.group | 14 |
| radius.control | 10 |
| radius.pill | 999 |
| radius.card（小组件） | 20 |
| radius.thumbnail | 16 |
| spacing 阶梯 | 4 / 8 / 12 / 16 / 24 / 32 |
| 设置窗口内边距 | 24（左右）、20（上）、24（下组距） |
| 行高（设置行） | 52（label+description 两行时 60） |
| 分组间距 | 20；区块间距 28 |
| 按钮高度 | primary 34；secondary 30；icon 32（命中 44） |

**材质**

| 表面 | 规格 |
|---|---|
| 设置窗口 | DesktopAcrylic 固定：白 tint、TintOpacity 0.86、Luminosity 0.5、Light；删除 pane 额外白填充；窗口外侧 1px `rgba(255,255,255,.55)` 内高光 |
| 设置分组 | `surface.group` 4.5% 墨色 + 无边框（或 1px `separator` 顶线） |
| 主外壳玻璃 | 保留现有 WGC→HLSL 管线；内容文字区加 4-6% tonal scrim；rim 0.75 DIP 左上光向 |
| 小组件卡 | 浅：白 12% + 1px 白 30% rim + `0 6 18 rgba(0,0,0,.10)`；深：白 6% + 白 rim；编辑态加 accent 1px 边 |
| 阴影 | 设置窗口 `0 16 48 rgba(0,0,0,.18)`；浮窗 `0 6 20 rgba(0,0,0,.16)` |

**动效**

| 场景 | 时长 | 曲线 |
|---|---|---|
| hover / 墨色变化 | 120ms | ease-out |
| press | 90ms | ease-out |
| 开关/滑块状态 | 180ms | ease-in-out |
| 页面切换 | 200-220ms | cubic-bezier(.2,0,0,1) |
| toast 进/出 | 160 / 200ms | ease-out / ease-in |
| 选中胶囊滑动 | 保留现有 28 rad 临界阻尼 | — |

---

## 5. 逐界面改版方案

### 5.1 设置窗口

**建议结构（方案 A：单列分组，macOS 式；推荐）**

```
┌──────────────────────────────────────────────┐
│              外观  交互  陪伴  插件  通用      │  ← 浮动导航（保留，48 高，选中=accent tint）
├──────────────────────────────────────────────┤
│  外观                                    ⬤ ✕ │  ← 页面标题 20/28；右上：隐藏组件(图标) + 关闭
│                                              │
│  玻璃                                        │  ← 分组标题 13/18 secondary
│  ┌────────────────────────────────────────┐  │
│  │ 模糊强度                        20 px  │  │  ← 行高 52，label 左 + value 右
│  │ ──────────●───────────────────────     │  │  ← 滑块 4 高，accent 轨道
│  │ 透明度                          76%    │  │
│  │ ───────────────────●──────────────     │  │
│  └────────────────────────────────────────┘  │  ← surface.group 4.5% 墨色，radius 14
│                                              │
│  显示内容                                    │
│  ┌────────────────────────────────────────┐  │
│  │ 显示时间                          ●──  │  │  ← macOS 式开关 42×24
│  │ 显示日期                          ○──  │  │
│  │ 显示星期                          ○──  │  │
│  └────────────────────────────────────────┘  │
│                                              │
│  动效                                        │
│  ┌────────────────────────────────────────┐  │
│  │ 简化动画                          ○──  │  │
│  │ 真实音频波浪条                    ●──  │  │
│  └────────────────────────────────────────┘  │
│                                              │
│  更改会自动保存                     恢复默认  │  ← 页脚只留 caption + 上下文动作
└──────────────────────────────────────────────┘
```

**改动点**

| 项 | 现在 | 改后 |
|---|---|---|
| 标题 | “GlassDesk” + 页面 H1 两处 | 只保留页面标题；窗口级操作靠右 |
| 关闭 | X + 页脚“关闭设置” | X + Esc；页脚只留“更改会自动保存” |
| 隐藏组件 | 36 高胶囊 | 32 图标按钮（eye-slash）＋tooltip；或 ghost 文本按钮 |
| 提示 | 常驻绿色 InfoBar | 中性 toast，1.6-2.4s 自动消失；错误常驻 |
| 分组 | 白卡 + 白边框 | 4.5% 墨色浅槽 + 14 圆角；无边框 |
| 列 | 2 列卡片网格（左右不等高） | 单列分组（内容宽 ≤ 560）；滑块可占满 |
| 控件 | WinUI 默认 | 统一定制（开关/滑块/按钮/输入/滚动条） |
| 导航 | 白胶囊选中，无 hover | accent 12% tint 选中 + accent.ink 字；hover 8% 墨；焦点环 2px |
| 窗口 | 880×680 | 约 720×680（只收窄宽度；窄窗单列不变） |
| 材质 | 跟随透明度的 4 层白 | 固定材质 2 层，任何桌面都保持同样观感 |

**逐页内容**

| 页面 | 分组 | 控件 |
|---|---|---|
| 外观 | 玻璃 / 显示内容 / 动效 | 2 滑块；3+2 开关；恢复默认 |
| 交互 | 展开与收起 / 行为 / 位置 | 2 滑块（值右侧）；1 开关；重置位置行（箭头图标） |
| 陪伴 | 形式 / 颜色 / 预览 | 3 形式卡（原生/猫耳/恶魔，各带迷你轮廓）；8 色板 + 自定义浮层；预览舞台（中性底，确保黑球可见） |
| 插件 | 功能 / 本地插件 | 液态玻璃开关；导入 secondary 按钮 + 一行信任提示；插件行（名称/版本/开关/“…”）；“第三方插件目录尚未开放”说明 |
| 通用 | 启动 / 快捷键 / 关于与诊断 | 2 开关；快捷键录制行；版本号 + 诊断路径（折叠） |

**Plugins 页本地化**：`Disabled` → “已停用/未启用”；导入按钮→“导入本地插件…”；列表项显示“名称 · 版本 · 状态”；底部 24 padding，避免被视口裁切。

**陪伴页预览舞台**：中性 12% 墨色底 + 8 圆角，黑球在浅色/白色预览都清晰；色板默认 `#202329` 加 7 个常用色；自定义浮层里放缩小版 ColorPicker（约 220×200）与 hex 输入。

### 5.2 主组件

**胶囊**：几何不动（时钟 200×52 / 音乐 208-296×52、陪伴 32、左边距 18）。只调：内容区 4% scrim、rim 0.75、时间墨色与字重按现有 token；频谱 8 条不动。

**时钟小展开（240×144）**

```
┌───────────────────────────┐
│                           │
│          23:35            │  clock.peekHero 34/44，中轴
│                           │
│      10月19日 周日         │  可选：12/16 caption（显示日期/星期时）
│                           │
│      ┌──────────────┐     │
│      │ 连接网易云音乐 ↗ │    │  28-32 高 ghost 按钮，中轴
│      └──────────────┘     │
│  ●                        │  陪伴随机落在边缘安全区（保留现行为）
└───────────────────────────┘
```

不改：240×144 几何、陪伴随机落点、时间白板不在此形态。

**音乐小展开（320×176）**：不改保护项（时间白板 `(68,14,184,32)`、8 根频谱、封面裁切）。只调 rim/scrim、按钮 hover、连接态文案。**此形态没有任何近期截图，Phase 3 必须补拍。**

**大展开（空态 360×300 / 播放 360×476，时间轴有效时 512）**

```
┌──────────────────────────────┐
│        ▬ ▬  ● ●  ▬ ▬         │  五槽黑胶囊导航 264×48@y16（几何/眼睛不动）
│             音乐              │  ← 页名 12/16 secondary @y68
│        ┌────────────┐        │
│        │  封面 192   │        │  @(84,88)，radius 20
│        └────────────┘        │
│       天球(そら)のMúsica       │  20/24 semibold @y296
│           Ave Mujica          │  14/20 secondary @y324
│        ─────────────          │  时间轴 248@(56,346)
│         1:12      3:48        │  12/18 caption
│        ‹      (▶)      ›      │  ← 播放 = 实心墨色 56 @y398；前后 = 幽灵 44
└──────────────────────────────┘
```

空态（360×300）：图标 44 绝对 y≈106 → “等待音乐”16/24 y≈162 → “连接网易云音乐”secondary 按钮 y≈231；保持无文字底板、无设置按钮、无“交互预览”。

**五槽导航（不改几何）**：48 高黑胶囊、264×48、spacing 38、眼睛 48×48、猫耳/恶魔身份全部保留。只做：
1. 导航下常驻当前页面名“音乐/小组件”（12/16 secondary，居中）；**位置从 y76 改到 y68**，避免与封面 y88 重叠；
2. 悬停其他槽时显示“小组件 · 预览”提示（已有实现，需验收）；
3. 保留眼睛/卫星环作为选中表达；**不新增槽内高亮**（中心槽在 distance=0 时 opacity 本来就是 0，加亮会破坏陪伴脸）；预览槽点击只提示“预览，音乐页保持”，不假装能切页；
4. 不做图标、不改槽数、不改 body 几何。

**小组件板（360 宽页面）**

```
┌──────────────────────────────┐
│  ▬ ▬ ● ● ▬ ▬                 │
│           小组件             │
│  我的组件          ＋ 添加    │  ← 标题 16/24；添加 = accent 胶囊
│                    编辑       │
│  ┌──────────┐  ┌──────────┐  │
│  │ 09:41    │  │ 19       │  │  ← 时间 36/44；日期 48/56 或 20/28
│  │ 周一     │  │ 10月19日  │  │
│  └──────────┘  └──────────┘  │  ← 轻纸面卡：白 12% + rim + 阴影
│                              │
└──────────────────────────────┘
```

编辑态：卡片轻纸面加深 + accent 1px 边；右上拖动柄；底部 3 个 44 图标按钮（配置/大小/移除）；“完成/取消”改为 accent 主按钮 + ghost。
空态：64 浅槽圆 + 时钟/日历图标 → “还没有组件”16/24 → “添加时间或日期开始”12/16 → accent“添加组件”。
添加菜单：浅色浮层 8 圆角、44 行高、图标 + 文案；位置对齐“添加”按钮。

### 5.3 窗口配置窗口（插件实例配置）

`WidgetInstanceConfigurationWindow.cs` 目前用原生 Button/Toggle/RadioButtons，与设置窗口两套语言。改版后统一到同一套控件与 token；不改保存/取消/时区行为。

---

## 6. 实施批次

| 批次 | 目标 | 主要文件 | 触及保护 | 验收标准 |
|---|---|---|---|---|
| 0 | 本文档 + HTML 样张 + 你批准 | `docs/design/` | — | 你确认方向与 Q1-Q6 |
| 1 | token 落地 + 修状态漏洞（不触 IA） | SettingsWindow.cs（颜色/资源/InfoBar/按钮三态）、SettingsFloatingNavigation.cs（hover/选中/焦点）、SettingsTypography.cs、TypographyCatalog.cs | 否 | 构建通过；hover/pressed 可触发；选中对比达标；无几何变化 |
| 2 | 设置窗口重构（需批准） | SettingsWindow.cs、SettingsColumns.cs、SettingsAcrylicBackdrop.cs、SettingsWindowShape.cs、WidgetInstanceConfigurationWindow.cs | 是 | 构建 + 窄窗 560×600 不破版；材质固定；窗口形 spike 结论 |
| 3 | 主组件视觉（需批准） | WidgetScene.cs、WidgetScene.Board.cs、DetailsNavigationVisual.cs、CompanionVisual.cs；native 仅当批准后单独批 | 是（材质/播放按钮/小展开动作） | 构建 + 静态对比度；实机截图由你或后续 QA 提供 |
| 4 | 小组件板首次验收 + 视觉 | WidgetBoardRenderer.cs、WidgetCore 相关 | 是（卡片方向） | 启动候选、截图、空态/编辑态/滚动/保存 |
| 5 | 收尾与回归 | tests（保护常量断言）、docs、publish 脚本 | — | Core 216、build、publish staging、更新文档与未测清单 |

**工作量估计**：批次 1 约 1-2 小时；批次 2 约 3-5 小时；批次 3 约 3-5 小时；批次 4 约 2-4 小时；批次 5 约 1-2 小时。执行中每批先构建、再自检、再交你确认。

**明确不做（本轮）**：
- 不改五槽几何、时间白板 `(68,14,184,32)`、8 根频谱、液态玻璃采集/着色器管线；
- 不改陪伴液体移动、随机落点、完整轮廓安全区；
- 不装字体、不引入第三方 UI 库、不升级 .NET/Windows SDK；
- 不做深色主题（先把浅色做对）、不做天气联网、不改插件 SDK 版本；
- 不覆盖旧发布包、不改正式配置、不改启动项/快捷键。

---

## 7. 保护项与需要你批准的事项

### 7.1 明确不动（无需批准，作为约束）

| 保护项 | 值/行为 |
|---|---|
| 五槽导航 | 264×48、spacing 38、黑胶囊 body、眼睛/猫耳/恶魔角几何、弹簧 220/280ms |
| 时间白板 | 音乐小展开 `(68,14,184,32)`；日期/星期开启时 40 高规则与填充策略 |
| 频谱 | 8 根短条、宽/间距/静止高度/锚点/SoftBar 分支 |
| 陪伴 | 单角色、液体移动参数、随机安全落点、完整轮廓在边框内 |
| 设置 IA 骨架 | 五分类及顺序（外观/交互/陪伴/插件/通用）；蓝色 #1689F9 仍为有效 accent |
| 技术栈 | C#/.NET 10/WinUI 3/Composition/薄 C++；不加依赖 |

### 7.2 需要你逐项批准（因为会改既有决定或 P1 布局）

| # | 事项 | 如果不批，如何降级 |
|---|---|---|
| R1 | 设置改“单列分组列表”（改 2 列卡片布局） | 保留 2 列，只统一卡片/间距/控件（观感改善约一半） |
| R2 | 去重页面标题、合并关闭入口、移除常驻 InfoBar | 保留现状，仅调 InfoBar 颜色与文案 |
| R3 | 设置窗口约 720×680 + 固定材质（不再跟随透明度滑块） | 保留 880×680 与现有材质，只减白层 |
| R4 | “隐藏组件”从标题栏胶囊改为 32 图标按钮 | 保留胶囊，仅统一尺寸/颜色 |
| R5 | 陪伴页：ColorPicker→色板+自定义浮层；ComboBox→3 形式卡 | 保留现有控件，只做紧凑排布 |
| R6 | 大展开播放控件改“实心播放 + 幽灵前后” | 保持三圆，仅 hover/墨色微调 |
| R7 | 时钟小展开连接动作改 ghost 按钮 | 保持裸文字，只加 chevron/下划线 hover |
| R8 | 五槽导航增加常驻页面名与预览提示（已在源码部分实现） | 维持现状，仅修选中亮度 |
| R9 | 小组件板卡片改“轻纸面”（从 4% 隐形卡提升） | 保持 4%，只加 rim 与编辑态强调 |
| R10 | 删除页脚“关闭设置”与开发期文案 | 仅隐藏开发文案，保留页脚按钮 |

---

## 8. 验证方案（含无法验证项）

### 8.1 我能做

1. `dotnet build`（已验证基线可编译）；每批后重建。
2. Core 测试（`dotnet run --project tests/GlassDesk.Core.Tests -c Release --no-restore -m:1`）；如改保护常量，补断言测试。
3. App publish 到新 staging（显式 ABI2 原生目录，`-p:GlassDeskUiPreview=true`，不覆盖旧产物）。
4. 静态对比度计算：用最终合成色算 WCAG 比值，输出表格（正文 4.5、大字 3、组件 3）。
5. HTML 视觉样张：`docs/design/quiet-glass-mockup.html`，浏览器打开可看方向（含设置/主组件/小组件板/token）。
6. `git diff` 逐批可审查；P1 常量可用 grep/测试证明未变。

### 8.2 我做不到（按你的说明跳过，不伪装）

- 实机 GUI 截图、点击/拖拽/悬停验证（无 computer use）。
- 混合 DPI、多屏、真实 100%/200%、Present 帧率、驻留/资源。
- 真实网易云连接与 SMTC 交互。
- 安装/升级/卸载。

### 8.3 建议的验收方式

1. 先看 HTML 样张，给方向反馈（可只批 R1-R10 的部分）。
2. 批次 1 后我构建 → 你本地跑 `artifacts/ui-preview/0.3.0-preview.2/GlassDesk.exe`（或我 publish 新 staging）看 hover/选中/材质。
3. 批次 2-4 每批生成新 staging，你本地打开确认；若你能截图发我，我再做像素级修正。
4. 最终由你或后续有 computer use 的会话完成实机验收；本计划不宣称 UI 验收通过。

---

## 9. 风险与回滚

| 风险 | 应对 |
|---|---|
| 设置重构引入布局回归（窄窗/滚动/焦点） | 批次 2 单独分支；窄窗 560×600 检查；键盘 ←/→/Tab 检查 |
| 玻璃改动破坏“透明度 bug”修复 | 只加 scrim/rim，不改 `TintOpacity` 与 HostFrame.Solid；grep 确认无实心回退 |
| native 改动导致 ABI/PRI 问题 | native 变更单独批；显式 ABI2 目录 publish；保留 529 文件哈希核对流程 |
| 视觉方向返工 | 先用 HTML 样张确认，再动 C#；每批可 `git checkout 9a9ccf8 -- <file>` 回滚 |
| 无实机验证导致“看起来不对” | 明确标注未验证；主组件改动尽量小步；批次 3 先做 managed-only |

---

## 10. 待你决策的问题

- **Q1 设置结构**：A 单列分组（推荐，macOS 式，改动中）/ B 保留 2 列只修视觉（改动小）/ C 左侧栏导航（最 macOS，但改动最大且偏离现有浮动导航）。
- **Q2 设置材质**：固定不透明度（推荐，保证任何桌面可读）/ 继续跟随透明度滑块。
- **Q3 五槽导航**：保留 5 槽 + 常驻页名 + 预览弱化（推荐）/ 只显示 2 个真实页 / 保持现状，仅修页名重叠与卫星语义。
- **Q4 播放控件**：实心主按钮 + 幽灵前后（推荐）/ 保持三圆只调 hover。
- **Q5 小组件板卡片**：轻纸面（推荐，可读、可拖动）/ 保持 4% 极淡。
- **Q6 深色主题**：本轮不做（推荐）/ 本轮一起做（工作量 +50%，且需重验全部截图）。

---

## 附录 A：保护值速查（禁止顺手改）

```
时间白板     (68,14,184,32) DIP；日期/星期 40 高规则；不叠 scrim
频谱         8 根；宽/间距/静止高度/锚点/SoftBar 分支
五槽导航     264×48；spacing 38；body/眼睛/耳朵/角几何；展开弹簧 220ms；重排 280ms
胶囊         时钟 200×52 / 音乐 208-296×52；陪伴 32；左边距 18
时钟小展开   240×144；时钟 Peek body 30 / 首选 (21.5,80)
音乐小展开   320×176
大展开       360×300（未连接）/ 360×476（播放）/ 512（时间轴有效）
             封面 192@(84,88)；标题/歌手 y296/324；时间轴 248@(56,346)；控制 y398/434
小组件页     宽 360；板高 304-512；卡 150×148 / 312×148；左 24；gap 12；顶 132；卡视口 H-156
accent       设置 #1689F9（有效蓝）；主组件内部现为 #5D6F84；卫星环 #1689F9
设置分类     外观/交互/陪伴/插件/通用（顺序固定）
```

## 附录 B：改动文件清单（按批次）

**批次 1**
- `src/GlassDesk.App/SettingsWindow.cs`
- `src/GlassDesk.App/SettingsFloatingNavigation.cs`
- `src/GlassDesk.App/SettingsTypography.cs`
- `src/GlassDesk.Core/TypographyCatalog.cs`（仅追加/角色映射，不改已有数值）

**批次 2**
- `src/GlassDesk.App/SettingsWindow.cs`（结构）
- `src/GlassDesk.App/SettingsColumns.cs`
- `src/GlassDesk.App/SettingsAcrylicBackdrop.cs`
- `src/GlassDesk.App/SettingsWindowShape.cs`
- `src/GlassDesk.App/WidgetInstanceConfigurationWindow.cs`

**批次 3**
- `src/GlassDesk.App/WidgetScene.cs`
- `src/GlassDesk.App/WidgetScene.Board.cs`
- `src/GlassDesk.App/DetailsNavigationVisual.cs`
- `src/GlassDesk.App/CompanionVisual.cs`
- （可选，单独批准）`native/GlassDesk.Native/*`（rim/scrim 实现）

**批次 4**
- `src/GlassDesk.App/WidgetBoardRenderer.cs`
- `src/GlassDesk.App/WidgetScene.Board.cs`（页面过渡）

**批次 5**
- `tests/GlassDesk.Core.Tests/*`（保护常量断言）
- `docs/*`（更新交互/验收文档）
- `scripts/build-ui-preview.ps1`（若需要新 staging 路径）

---

## 附录 C：HTML 样张与预览图

文件：
- `docs/design/quiet-glass-mockup.html`（浏览器直接打开，离线，无外部依赖；右上角可切换“浅色/深色/花背景”；`?part=1..6` 可只看某一节）
- `docs/design/preview/hero.jpg`（主组件三态）
- `docs/design/preview/settings-appearance.jpg`（设置·外观）
- `docs/design/preview/settings-companion.jpg`（设置·陪伴）
- `docs/design/preview/settings-plugins.jpg`（设置·插件）

内容：主组件三态、设置窗口（外观/陪伴/插件）、小组件板、token 表与改版理由。
注意：样张是 CSS 绘制的方向示意，不是最终 WinUI 像素；玻璃模糊、液态折射与真实桌面透景无法在样张中完全还原。批准后按第 6 节分批落地。
