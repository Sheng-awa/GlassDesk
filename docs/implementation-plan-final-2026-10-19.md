# GlassDesk UI 最终实施计划（交接给新会话）

日期：2026-10-19（Asia/Shanghai）
状态：**计划已定，等待执行。本会话只做规划，未改任何 UI 源码。**
执行者：新会话（编码 agent）。本文档必须能脱离聊天记录独立执行。

---

## 0. 30 秒看懂

1. **UI 目标**：简洁、安静、圆角统一；功能一个都不减（时间/日期卡、插件、配置、诊断、播放、频谱、陪伴都保留）。
2. **最终布局**：大展开里，**顶部只留陪伴 = 首页把手**（点它收起回时间胶囊），**导航搬到卡片底部**，做成 5 项浮动 Dock（音乐、小组件、时钟、专注、随记）；右上角圆按钮删除。
3. **圆角统一四档**：24（外壳）/ 16（卡片、封面）/ 10（输入框、色块、菜单）/ 全圆（按钮、开关、名字牌）。
4. **设置窗口**：保留五分类、两列布局、全部功能；只去噪（删重复标题/关闭口/绿条/开发文案、固定材质、修 3 个裁切 bug、统一圆角）。
5. **分 4 个阶段**：设置与圆角 → 详情页 chrome（把手 + Dock）→ 小组件板 → 可访问性/测试/收尾。每阶段单独 commit、构建、截图验收。
6. **默认决策（已按推荐锁定，不再询问）**：Dock（不是全宽 Tab Bar）、圆角 24/16/10、设置两列、顶部把手点击回胶囊、预览项不切页。用户若中途改主意，按新指令调整。

---

## 1. 环境与基线

### 1.1 项目位置

```
项目根：D:\Believe_in_yourself\about AI\work about agents\共同工作区\GlassDesk
源码：  src\GlassDesk.App（WinUI 3 / C#）
        src\GlassDesk.Core（契约/排版/页面状态/小组件）
        src\GlassDesk.Platform / NativeBridge / Plugins
        native\GlassDesk.Native（C++，本计划**不改**）
测试：  tests\GlassDesk.Core.Tests 等
文档：  docs\
```

### 1.2 Git 基线

- 仓库在项目根 `.git`。
- 已有提交：
  - `9a9ccf8` 源码基线（272 文件）
  - `ddbf867` / `4b35b62` / `8cbe365` / `aa0c339` 设计文档
- **开工前**：
  ```powershell
  cd "D:\Believe_in_yourself\about AI\work about agents\共同工作区\GlassDesk"
  git status                # 必须干净
  git checkout -b ui/final  # 每阶段一个 commit
  ```
- 回滚：`git checkout <上一个 commit> -- <文件>`；整阶段回滚 `git revert <commit>`。

### 1.3 工具链（已验证）

- .NET SDK **10.0.401**（`global.json` 锁定，rollForward=disable）
- Windows SDK 10.0.26100.0，Windows App SDK 2.5.1
- 基线命令已验证可编译：
  ```powershell
  dotnet build tests/GlassDesk.Core.Tests/GlassDesk.Core.Tests.csproj -c Release -m:1
  ```

### 1.4 构建 / 发布命令

Core 测试：

```powershell
dotnet run --project ./tests/GlassDesk.Core.Tests/GlassDesk.Core.Tests.csproj -c Release --no-restore -m:1
```

App publish（必须显式 ABI2 原生目录；输出到**新 staging**，不要 publish 到运行中的目录）：

```powershell
dotnet publish ./src/GlassDesk.App/GlassDesk.App.csproj `
  -c Release -r win-x64 --self-contained true --no-restore `
  -p:GlassDeskUiPreview=true -p:Version=0.3.0-preview.3 `
  '-p:GlassDeskNativeDirectory=D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/artifacts/native-ui-preview/0.3.0-preview.2' `
  -p:PublishSingleFile=false -p:PublishTrimmed=false `
  -o ./artifacts/qa/ui-final/staging -m:1 -nr:false
```

> 原生目录已存在：`artifacts/native-ui-preview/0.3.0-preview.2/GlassDesk.Native.dll`。本计划不改 native，不需要重建 DLL。

### 1.5 启动与截图（没有 computer use 也能验收）

**已验证可用**：以隔离数据目录启动预览，再用 DPI-aware PowerShell 抓窗口区域。不要截整屏、不要把带用户桌面的截图提交到 Git。

1）启动：

```powershell
$exe = "D:\Believe_in_yourself\about AI\work about agents\共同工作区\GlassDesk\artifacts\qa\ui-final\staging\GlassDesk.exe"
$data = "D:\Believe_in_yourself\about AI\work about agents\共同工作区\GlassDesk\artifacts\qa\ui-final\data"
New-Item -ItemType Directory -Force $data | Out-Null
Start-Process -FilePath $exe -ArgumentList @("--ui-preview","--data-dir=$data") -WorkingDirectory (Split-Path $exe)
Start-Sleep -Seconds 4
```

2）打开设置：再启动**第二个实例**（同一命令行），它会请求已运行实例打开设置窗口：

```powershell
Start-Process -FilePath $exe -ArgumentList @("--ui-preview","--data-dir=$data") -WorkingDirectory (Split-Path $exe)
Start-Sleep -Seconds 2
```

3）DPI-aware 抓窗口（保存到 `artifacts/qa/ui-final/screens`，不入库）：

```powershell
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;using System.Text;
public class GDW {
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[void][GDW]::SetProcessDPIAware()
$out = "...\artifacts\qa\ui-final\screens"; New-Item -ItemType Directory -Force $out | Out-Null
$procs = @(Get-Process -Name GlassDesk -ErrorAction SilentlyContinue)
$rows = New-Object System.Collections.ArrayList
$cb = [GDW+EnumProc]{ param($h,$l) $pid2=0;[void][GDW]::GetWindowThreadProcessId($h,[ref]$pid2)
  if($procs.Id -contains $pid2){ $sb=New-Object System.Text.StringBuilder 256;[void][GDW]::GetWindowText($h,$sb,256)
    $r=New-Object GDW+RECT;[void][GDW]::GetWindowRect($h,[ref]$r)
    if([GDW]::IsWindowVisible($h) -and ($r.Right-$r.Left) -gt 10){
      [void]$rows.Add([pscustomobject]@{Title=$sb.ToString();L=$r.Left;T=$r.Top;W=$r.Right-$r.Left;H=$r.Bottom-$r.Top})}}; return $true }
[void][GDW]::EnumWindows($cb,[IntPtr]::Zero)
foreach($t in $rows){ if($t.Title -like '*Shadow*'){continue}
  $bmp=New-Object System.Drawing.Bitmap(($t.W+4),($t.H+4)); $g=[System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen(($t.L-2),($t.T-2),0,0,$bmp.Size)
  $name=($t.Title -replace '[^\w\u4e00-\u9fa5]+','-')+"-$($t.W)x$($t.H).png"
  $bmp.Save((Join-Path $out $name),[System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose() }
```

4）点击展开：
- 已验证：向胶囊窗口 `PostMessage` `WM_LBUTTONDOWN/UP`（客户端坐标中心）可以展开；但**指针离开后详情可能自动收起**。
- 需要稳定看到详情/点击 Dock 时：用真实指针 `SetCursorPos` + `mouse_event(LEFTDOWN/UP)`，操作前保存 `GetCursorPos`，结束后还原；动作控制在 3–5 秒内。参考脚本见附录 B。
- 抓完后关闭预览实例（任务管理器结束 `GlassDesk.exe`，或关闭全部窗口）。

5）截图检查尺寸（150% DPI 预期，±4 px）：
| 形态 | 物理窗口尺寸 |
|---|---|
| 时钟胶囊 | 300×78 |
| 大展开空态（360×300） | 540×450 |
| 音乐大展开（360×476） | 540×714 |
| 小组件页（360×512） | 540×768 |
| 设置窗口 | 1320×1020 |

> 截图里会透出桌面背景，属正常；**不要**把截图提交 Git。

---

## 2. 最终设计规范

### 2.1 详情页布局（核心结构改动）

**顶部：陪伴 = 首页把手**
- 尺寸 58×28，圆角 14（全圆），深色 `#202328`，居中，top=14。
- 内部两只眼睛（5–6×8–9 圆角条），白色 `#FAFBFC`。
- 悬停：显示白色名字牌“回首页”（20 高、全圆、11–12 DIP 字）。
- 点击：收起回**时间胶囊**（调用 `WidgetActions.Close`，Core 已把它映射为 `ChangeForm(Capsule)`）。
- 键盘/UIA：名称“回首页（陪伴）”，Enter 触发。

**底部：浮动 Dock**
- 320×48，圆角 24，深色 `#202328`，居中，bottom=16。
- 5 项（从左到右）：
  1. 音乐（真页面）
  2. 小组件（真页面）
  3. 时钟（预览）
  4. 专注（预览）
  5. 随记（预览）
- 每项中心 x（Dock 内，DIP）：32 / 96 / 160 / 224 / 288（5 等分，内部宽 320）。
- 未选中：图标 18×18，白色 60%；预览项 65%。
- 选中：白色胶囊 88×36、圆角 18，图标 + 名字（11 DIP/500）；名字只显示在选中项。
- 悬停：上方 20 高白色名字牌，显示该项名字；预览项名字后加“（预览）”。
- 点击真页面：切页；点击预览项：不切页（可发一个轻提示“预览页面，尚未实现”，或仅高亮；不做假页面）。
- UIA：5 个目的地 + 名称；预览项名称含“预览”。
- 键盘：←/→ 移动焦点，Enter 激活；Esc 收起（如果现有 Esc 逻辑已存在则复用）。

### 2.2 圆角统一（四档）

| 档位 | 值 | 用在哪 |
|---|---|---|
| 外壳 | **24** | 组件外壳（胶囊/小展开/大展开）、设置面板、设置顶部丝带外框、Dock |
| 卡片 | **16** | 设置卡片、封面、小组件卡、菜单浮层、预览舞台 |
| 控件 | **10** | 输入框、下拉框、色块、ColorPicker、图标底 |
| 全圆 | **半高/2** | 所有按钮、开关、名字牌、选中胶囊、首页把手 |

需要修改的现有值（全部项）：

| 位置 | 现在 | 改成 |
|---|---|---|
| 设置卡片 `SettingsWindow.cs:1113` | 24 | 16 |
| 设置输入框 `:980,1059,1163` | 24 | 10 |
| 关闭按钮 `:307` | 20 | 全圆（20）不变，保留 |
| 提示/onboarding `:351` | 10 | 10（保留） |
| 设置丝带外框 `SettingsFloatingNavigation.cs:40` | 28 | 24 |
| 丝带选中底 `:47` | 23 | 20（同心：24−4） |
| 封面 `WidgetScene.cs:740`（Details） | 20 | 16 |
| 小组件卡 `WidgetBoardRenderer.cs:30` | 20 | 16 |
| Widget 配置窗按钮 `WidgetInstanceConfigurationWindow.cs:239,335` | 24 | 全圆 |
| 色块/ColorPicker | 直角/默认 | 10 |
| Dock / 首页把手 | — | 24 / 14（全圆） |

同心规则：紧贴两层 `内 = 外 − 间距`，最小 8；大块卡片固定 16，不强行套公式。条形元素（频谱、进度、导航小条、时间轴）保持 2–4，不动。

### 2.3 颜色 / 墨色 / 材质

- **墨色**沿用 `TypographyCatalog.Ink()` 四档（浅色：`#20252C/#48515B/#626C78/#8E98A3`；深底四档已有）。设置页统一用它，不再散落灰值。
- **accent**：
  - `accent.fill = #1689F9` 只用于填充、滑块轨道、开关 on、图标（组件对比 ≥3:1）。
  - `accent.ink = #0B6BCB` 用于小字/选中文字（对白底 ≥4.5:1）。
- **设置窗口材质**：`SettingsAcrylicBackdrop` 固定 `TintOpacity = 0.86`，不再跟“透明度”滑杆；Theme 恒 Light；删除 pane 额外白填充或减到只剩一层；设置卡片面用 4.5% 墨色浅槽 + 无边框。
- **主外壳玻璃**：不改 native 材质；只保证文字/控件不新增白底板。不要回退“实心不透明”修复。
- **组卡纸面**（小组件板）：浅色 白 12% + 1px rim + 轻阴影；深色 白 6%。
- **阴影**：设置窗口保持系统阴影；Dock/把手可用 0 6 18 rgba(0,0,0,.18)。
- **不做深色主题**（本轮）。

### 2.4 排版

设置窗口（全部用现有 Segoe UI Variable，不装字体）：

| 角色 | 字号/行高 | 字重 |
|---|---|---|
| 页面标题 | 20/28 | 600（CJK 500） |
| 分组标题 | 13/18 | 600，secondary 色 |
| 行标题 | 14/20 | 400 |
| 行说明 | 12/16 | 400 |
| 值 | 14/20 | 500，tabular |
| 导航 | 13/18 | 400/500 |
| 按钮 | 13/18 | 500 |

主组件继续用 `TypographyCatalog` 现有角色（clock.capsule 25、clock.peekHero 34、media.detailsTitle 20、widget.clock 36 等），不改字号。

### 2.5 间距 / 动效 / 状态

- 间距阶梯 4/8/12/16/24/32。
- 动效：hover 120ms、press 90ms、选中/切页 180–220ms、`cubic-bezier(.2,0,0,1)`；减少动态效果时直接完成。
- 所有可点击项必须有 Normal / Hover / Pressed / Disabled / Focus 可见状态。
- 焦点环：2px `#1689F9` + 1px 白色内间隔（仅键盘）。
- 命中区：视觉可小，命中 ≥44×44（Dock 项 44×44；首页把手 44×44）。

---

## 3. 详情页施工图（精确坐标）

统一：卡片宽 360，坐标原点左上，单位 DIP。

### 3.1 音乐页（360×476）

| 元素 | 位置/尺寸 | 说明 |
|---|---|---|
| 首页把手 | (151,14,58,28) | center x=180 |
| 封面 | (96,70,168,168) | 圆角 16 |
| 标题 | (24,246,312,24) | 20/24 semibold，居中 |
| 歌手 | (24,274,312,20) | 14/20 secondary，居中 |
| 时间轴 | (56,300,248,38) | 轨道 4 高，标签 12/18 |
| 播放控制 | 上一首 (96,346,44,44)；播放 (152,340,56,56)；下一首 (220,346,44,44) | 居中组 |
| Dock | (20,412,320,48) | bottom=16 |

### 3.2 小组件页（360×512）

| 元素 | 位置/尺寸 | 说明 |
|---|---|---|
| 首页把手 | (151,14,58,28) | 同上 |
| 标题“我的组件” | (24,62,220,24) | 16/24 semibold |
| 编辑 / 添加 | 右对齐，y=58–86；编辑 ghost，添加 accent 胶囊 | 高 28–30 |
| 卡片 | 起点 (24,104)；小卡 150×148、中卡 312×148；间距 12 | 两小 + 一宽；可滚动 |
| Dock | (20,448,320,48) | bottom=16 |

### 3.3 空态（未连接，360×300）

| 元素 | 位置/尺寸 | 说明 |
|---|---|---|
| 首页把手 | (151,14,58,28) | 同上 |
| 音乐图标 | (158,62,44,44) | 浅槽圆 44 |
| “等待音乐” | (100,118,160,24) | 14/20 |
| 连接按钮 | (100,162,160,36) | secondary 按钮，圆角全圆 |
| Dock | (20,236,320,48) | bottom=16 |

> 空态内容进入 `DetailsBodyViewport`（当前 viewport offset y=80）：图标/文字 body 坐标分别为 (158,-18)/(100,38) 会越界。建议空态把 `_detailsViewport.Offset` 改为 (…,56)，或在 `DrawDetailsBody` 里改画到 62/118；二选一，截图确认不裁切。

### 3.4 与当前实现的差异（必须改）

| 项 | 现在 | 目标 |
|---|---|---|
| 顶部导航 | `DetailsNavigationVisual` 264×48 @y16，5 竖条 + 眼睛 + 卫星 28 + 页名 @y76 | 删除；改为顶部把手 + 底部 Dock |
| 眼睛 | 在导航长条里 | 在首页把手（58×28）里 |
| 右上圆按钮 | 卫星，28×28 + 44 命中，`WidgetsAction=111` | 删除 |
| 页名 | y76 的 264×20 文本 | 删除；名字在 Dock 选中项/悬停牌 |
| 音乐页高度 | 476（时间轴有效时 512） | **476 恒定** |
| 小组件页高度 | 304–512 自适应 | **512 恒定** |
| 详情内容 | 封面 (84,88,192)、标题 y296、时间轴 y346、控制 y398/434 | 见 3.1 |

**保护项（不动）**：时间白板 `(68,14,184,32)`、8 根频谱、陪伴液体运动、播放/暂停动画、胶囊/小展开、耳朵/角、插件 API。

---

## 4. 分阶段施工（每阶段独立 commit）

### Phase 1 · 设置窗口与圆角（不改结构，低风险）

**目标**：设置窗口从“灰、乱、裁切”变成“安静、清楚”；圆角四档落地。

改动文件：
- `src/GlassDesk.App/SettingsWindow.cs`
- `src/GlassDesk.App/SettingsFloatingNavigation.cs`
- `src/GlassDesk.App/SettingsAcrylicBackdrop.cs`
- `src/GlassDesk.App/WidgetInstanceConfigurationWindow.cs`（只改圆角/控件对齐）
- 新增 `src/GlassDesk.App/UiRadius.cs`（四档常量，供上述代码引用；也可直接用常量，但建议统一）

具体步骤：

1. **新建 `UiRadius` 常量**：
   ```csharp
   internal static class UiRadius
   {
       public const double Shell = 24, Card = 16, Control = 10;
   }
   ```
   把设置卡片、输入框、按钮、导航、配置窗的硬编码半径替换（见 2.2 表）。

2. **设置材质固定**：
   - `SettingsAcrylicBackdrop` 构造参数不再从用户“透明度”读；固定 `0.86`。
   - 搜索 `new SettingsAcrylicBackdrop(` 与 `SetTintOpacity(`：构造处传常量；删除/忽略透明度滑杆对 backdrop 的联动（滑杆仍影响组件玻璃，不删功能）。
   - `SettingsWindow` 的 pane/卡片白层：pane 填充减为 0 或保留极淡；卡片改 4.5% 墨色浅槽（资源字典里定义 `SettingsGroupBrush`），卡片边框去掉或降到 1px 8% 墨。

3. **去重复/去噪**：
   - 删除 header 里的“GlassDesk”大标题，header 只留页面名（`外观/交互/…`）与右侧按钮；减少 header 高度。
   - 关闭入口只留右上 X（+ Esc）；页脚删除“关闭设置”按钮，页脚左侧保留“更改会自动保存”。
   - “隐藏组件”从 36 高胶囊缩成 32 高小按钮（功能保留），放在 X 左边。
   - `InfoBar`：默认关闭；成功提示用 `InfoBarSeverity.Informational` + 自动 2.4 秒关闭；错误保留常驻；不要绿色常驻条。
   - 本地化“Disabled”（`SettingsWindow.cs:869-870` → `native/GlassDesk.Native/LiquidGlass.cpp:224` 的原始字符串）：在 UI 层做映射（例如“已停用/未启用”），不改 native。
   - 删除用户界面中的开发期文案（“UI 预览不修改…”等），或收进“关于/诊断”折叠区。

4. **修 3 个实机 bug**：
   - 外观页“显示星期”被底部裁切：每个页面 `ScrollViewer` 底部 padding ≥24；卡片与页脚之间留 16；必要时调小卡片内边距。
   - 插件列表被视口裁切：同样补底部 padding，并确保 `_pluginList` 在外层 ScrollViewer 内完整滚动。
   - ColorPicker 直角且太大：包裹在 `Border`（圆角 10）+ `Clip`（`RectangleGeometry`，SizeChanged 更新）；高度限制约 240；**不要**删除颜色功能。

5. **按钮状态修复**：
   - `SettingsWindow.cs:1048-1049` 三态同色 → 分开：Normal `0x86FFFFFF`、PointerOver `0x9EFFFFFF`、Pressed `0xB4FFFFFF`、Disabled `0x40FFFFFF`（按实际白底对比微调）。
   - `SettingsFloatingNavigation`：`buttonGrid.IsHitTestVisible=false` 导致 hover 永不触发 → 用父级 `PointerMoved` 计算悬停项并给按钮/底板加 hover 视觉；保留拖拽与键盘。
   - 选中态：白胶囊改为 `accent.tint`（12%）+ `accent.ink` 文字；外框圆角 24，选中底 20。

6. **验证**：
   ```powershell
   dotnet build tests/GlassDesk.Core.Tests/GlassDesk.Core.Tests.csproj -c Release -m:1
   # publish staging（见 1.4）
   # 启动 + 第二实例打开设置 + 截图（见 1.5）
   ```
   验收：设置窗口 1320×1020；页面无裁切；按钮 hover 可见；选中项 accent；材质不再随透明度变灰；无绿色常驻条；无 “Disabled”；X 可关；Esc 可关。

**Phase 1 不做**：不改设置窗口布局、不改 IA、不改窗口尺寸、不动 ColorPicker 功能。

---

### Phase 2 · 详情页 chrome（顶部把手 + 底部 Dock）

**目标**：实现第 2、3 节的布局；这是唯一的结构性改动。

改动文件：
- 新增 `src/GlassDesk.Core/DetailsChromeLayout.cs`
- 修改 `src/GlassDesk.Core/UiPreviewContracts.cs`（新增 `DetailsDockItems`）
- 修改 `src/GlassDesk.Core/Contracts.cs`（新增 `WidgetActions.DockFirst=120`，追加不重编号）
- 新增 `src/GlassDesk.App/DetailsChromeVisual.cs`
- 修改 `src/GlassDesk.App/WidgetScene.cs`
- 修改 `src/GlassDesk.App/WidgetScene.Board.cs`
- 保留 `DetailsNavigationVisual.cs` 暂时不删，编译通过后再清理
- 测试：`tests/GlassDesk.Core.Tests/` 中与导航/尺寸/板高相关的断言

#### 2.1 Core：布局常量 + Dock 数据

`DetailsChromeLayout.cs`（建议内容）：

```csharp
public static class DetailsChromeLayout
{
    public const float CardWidth = 360;
    public const float HomeWidth = 58, HomeHeight = 28, HomeTop = 14, HomeRadius = 14;
    public const float DockWidth = 320, DockHeight = 48, DockBottom = 16, DockRadius = 24, DockItemWidth = 61.6f;
    public const float DockActiveWidth = 88, DockActiveHeight = 36, DockActiveRadius = 18;
    public static FloatRect Home(Vector2 size) => new((size.X - HomeWidth) / 2, HomeTop, HomeWidth, HomeHeight);
    public static FloatRect Dock(Vector2 size) => new((size.X - DockWidth) / 2, size.Y - DockBottom - DockHeight, DockWidth, DockHeight);
    public static FloatRect DockItem(Vector2 size, int index)
    {
        var d = Dock(size); float c = d.X + DockItemWidth * (index + .5f);
        return new(c - 22, d.Y, 44, DockHeight);
    }
}
```

`UiPreviewContracts.cs` 新增：

```csharp
public sealed record DetailsDockEntry(string Id, string Label, int ActionId, bool IsPage);
public static class DetailsDockItems
{
    public const int Music = 0, Widgets = 1, Clock = 2, Focus = 3, Notes = 4;
    public static IReadOnlyList<DetailsDockEntry> All { get; } = Array.AsReadOnly(new[]
    {
        new DetailsDockEntry("music", "音乐", WidgetActions.DockFirst + Music, true),
        new DetailsDockEntry("widgets", "小组件", WidgetActions.DockFirst + Widgets, true),
        new DetailsDockEntry("clock", "时钟", WidgetActions.DockFirst + Clock, false),
        new DetailsDockEntry("focus", "专注", WidgetActions.DockFirst + Focus, false),
        new DetailsDockEntry("notes", "随记", WidgetActions.DockFirst + Notes, false)
    });
}
```

`Contracts.cs` 的 `WidgetActions` 追加（不要改旧值）：
```csharp
public const int DockFirst = 120; // 120..124
```

#### 2.2 App：`DetailsChromeVisual`

职责：**顶部把手 + 底部 Dock**；替代 `DetailsNavigationVisual` 的视觉/命中/UIA/动效。不要继承旧类，按新尺寸重写。

建议 API（与 `WidgetScene` 对接）：

```csharp
internal sealed class DetailsChromeVisual : IDisposable
{
    public ContainerVisual Visual { get; }
    public bool NeedsTick { get; }
    public event Action<string>? PageRequested;   // "music" / "widgets"
    public void SetLayout(float width, float height);
    public void BeginOpen(long sequence, double now, bool reducedMotion);
    public void Close();
    public void SetReducedMotion(bool reducedMotion, double now);
    public void PointerMoved(float x, float y, double now);
    public void PointerExited(double now);
    public int HitTest(float x, float y);          // 返回 WidgetActions.Close 或 DockFirst+i，-1 无
    public bool Activate(int actionId, double now);
    public void Tick(double now);
    public IReadOnlyList<AccessibleControl> GetAccessibleControls();
    public FloatRect HomeBounds { get; }
    public FloatRect DockBounds { get; }
}
```

实现要点（最低可行）：
- 视觉层级：`Visual` 容器 → 首页把手（`ShapeVisual` 圆角矩形 + 两条眼睛形状）→ Dock 背景（`SpriteVisual` + `RenderSurface` 320×48）→ 选中胶囊（`ShapeVisual` 白色圆角矩形，`Offset` 动画）→ 悬停名字牌（`SpriteVisual` + `RenderSurface`，20 高）。
- Dock 背景/图标/文字用 `host.RenderSurface` + `SurfaceOp`（圆角矩形、`Icon`、`Typography`）一次画好；**只在选中/悬停变化时重画**，不要每帧重画。
- 选中胶囊用 Composition `Offset` 动画 180ms；悬停牌用 `Opacity` 120ms；减少动态效果时立即设置。
- 命中：
  - 首页把手矩形 + 44×44 命中 → `WidgetActions.Close`；
  - Dock 每项 44×44 → `DockFirst + i`；
  - 其余返回 -1。
- 激活：
  - `WidgetActions.Close`：不在这里处理（返回给 `WidgetScene` 后由 Core `WidgetController.HandlePointer` 收起；见 2.3 路由）。
  - `DockFirst+0`：`PageRequested("music")`；
  - `+1`：`PageRequested("widgets")`；
  - `+2..+4`：预览项，不切页；可以只高亮或返回 false（推荐只高亮 + UIA 名称带“预览”）。
- UIA：6 个控件（回首页 + 5 项），坐标为实际 bounds；键盘 ←/→ + Enter 可在 Dock 内移动/激活（`AccessibleControl` 与现有 `WidgetScene` 键盘路径保持一致）。
- 高对比：用 `UISettings` 系统色替换深色/白色；减少动态效果时无动画。
- 首页眼睛：静态两条圆角条即可；悬停时宽度 +1px（可选）。**不要**接入 `CompanionVisual`（避免与导航形变耦合）；陪伴的液体动画继续只在胶囊/小展开。

#### 2.3 WidgetScene 集成点（逐处）

| 位置 | 现在 | 改成 |
|---|---|---|
| 字段/构造 | `DetailsNavigationVisual _navigation` | `DetailsChromeVisual _chrome`；插入到 `_root` 顶层 |
| `SceneMotionActive` | `_navigation.NeedsTick` | `_chrome.NeedsTick` |
| `SetCharacter` | 调 `_navigation.SetCharacter(...)` | 删除（详情不再有独立脸） |
| `_navigation.SetReducedMotion` | | `_chrome.SetReducedMotion(...)` |
| `HitTest` | `_navigation.HitTest` | `_chrome.HitTest` |
| `SetLayout` / `BeginOpen` / `Close/EndDetails` | `_navigation.*` | `_chrome.*` |
| `PointerMoved/Exited` | `_navigation.*` | `_chrome.*` |
| `RouteSpecialPointer` | 处理 99–104 / 111 | 增加 `action is >= WidgetActions.DockFirst and <= WidgetActions.DockFirst+4` → `_chrome.Activate(action,now)`；旧 111 逻辑删除 |
| `GetAccessibleControls` | `_navigation.GetAccessibleControls()` | `_chrome.GetAccessibleControls()` |
| 页请求 | `_navigation.RealPageRequested` | `_chrome.PageRequested` |
| `DetailsPageRequested` | 不变 | 不变 |
| `SetWidgetBoard` | `_navigation.SetRealPage(...)` | 删除（Dock 自己按 `_detailsPageId` 高亮；由 `WidgetScene` 把当前页传给 chrome，新增 `SetCurrentPage(string)`） |

页面高亮：`DetailsChromeVisual.SetCurrentPage("music"/"widgets")`，选中胶囊落在对应项。

#### 2.4 WidgetScene 布局改动（精确值）

1. `ContentPolicy.cs`：Details 保持 360×300（空）/360×476（播放）；不改。
2. `WidgetController.cs`：
   - 删除 `hasTimeline?512` 逻辑（第 45 行）；音乐详情恒 476。
   - `SetDetailsPageHeight` 仍用于小组件页；小组件页固定 512（见 2.5）。
3. `WidgetScene.cs`：
   - `ArtworkPoseFor(Details)` → `(96, 70, 168, 168, 16)`（第 740 行）。
   - `UpdateMetadata` details 分支：`layer.Visual.Offset = new(24, 166, 0)`；标题 `Text(..., 0,0,312,24, MediaDetailsTitle, center:true)`；歌手 y=28（绝对值 274）；高度 54（第 1197-1199 行）。
   - `DetailsTransportY` → `340`（第 1309 行）。
   - 时间轴：`_timeline.Offset = new(56, 300, 0)`（第 617 行）；track 不变。
   - `DrawDetails`：控制坐标 `Transport(96,346,44)`、`Transport(152,340,56)`、`Transport(220,346,44)`（第 1148-1150 行）；空态“打开网易云音乐”改 secondary 按钮 (100,206,160,36)，不要裸文字。
   - `BuildControls`：同步新的上一首/播放/下一首坐标（第 1272-1274 行）；Seek 控制 y=300（第 1278 行）；空态 OpenMusic 控制 (100,206,160,36)（第 1281 行）。
   - `_detailsViewport.Offset` 保持 (…,80)；`_detailsBody.Size` 音乐 326、空态 112（第 505 行）；`DetailsBodyViewportHeight` → 326/112（第 748 行）。
   - 空态 `DrawDetailsBody`：图标绝对 y62 → body y=-18 越界；改为 `_detailsViewport.Offset=(…,56)`（第 507、702 行同步）或把内容画到 body 内可见区域；两处必须一致，用截图确认。
   - 受保护区域（第 806 行）：详情导航保护区 `new(36,12,288,68)` 改为 `new(20,8,320,40)`；新增 Dock 保护区 `new(16, height-72, 328, 72)`（若陪伴在该形态不显示，可为空，但保留避免未来重叠）。
   - `DetailsBodyViewportHeight` 与 `_artworkViewport` 的 clipBottom（第 763 行）同步。
4. `WidgetScene.Board.cs`：
   - 小组件页高度：`PrepareWidgetBoard` 中 `height = 512`（固定，去掉 clamp 304）；
   - `BoardNaturalHeight` → `512f`（或 clamp(…,512,512)）；
   - `_boardCards.Offset` y=104；`_boardCards.Size = (size.X, size.Y-176)`（512−176=336）；
   - `ScrollBoard` 的 maximum 用 `size.Y-176`；
   - `ApplyBoardVisibility` 的 offset y=104；
   - `LayoutSize`/`BoardNaturalHeight` 统一 512。
5. `WidgetBoardRenderer.cs`（Phase 3 的视觉，先保证不裁切）：
   - `CardsTop` 132→104，`BottomInset` 24→72；
   - toolbar 标题 y=62、按钮 y=58、卡片 y=104；
   - 菜单对齐“添加”按钮右缘；空态垂直居中（坐标见 Phase 3）。
6. `GlassDeskCoordinator.Widgets.cs`：`PrepareWidgetBoard` 的 height 固定 512（或调用 `WidgetScene` 的 BoardNaturalHeight 已改）。

#### 2.5 验收

- 构建通过；Core tests 更新后通过。
- 启动截图：
  - 空态：540×450；顶部把手、Dock 完整，不裁切。
  - 音乐页：540×714；封面/标题/时间轴/控制/Dock 均不重叠；Dock 底距 16。
  - 小组件页：540×768；卡片不被 Dock 遮；Dock 选中“小组件”。
- 点击：Dock 音乐/小组件切页；预览项不切页；首页把手收起回胶囊。
- 键盘/UIA：Tab 能到 6 个控件；名称正确。
- 对比图：`docs/design/preview/details-layout-options.png`（允许 ±2 DIP 偏差；截图给用户看）。

**Phase 2 不做**：不做 Dock 的液体/视差特效；不改 native；不新增真实页面。

---

### Phase 3 · 小组件板与内置卡片（视觉收干净）

改动文件：
- `src/GlassDesk.Core/WidgetBoardBuiltIns.cs`
- `src/GlassDesk.App/WidgetBoardRenderer.cs`
- `src/GlassDesk.App/WidgetScene.Board.cs`（微调）
- `tests/GlassDesk.Core.Tests` 中卡片文本断言

#### 3.1 卡片文字：3–4 层 → 2 层

时间卡（Small）：
- 删除“时间”标签元素（不再占第一层）。
- 大值 `WidgetClock`：`(12, 34, width, 44)`，如 `02:23`。
- 说明 `WidgetLabel`：`(12, 112, width, 16)`，内容用 `ZoneCaption(zone)`（如 `UTC+08:00`；不要长城市名）。
- 不再渲染长时区字符串。

日期卡（Small）：
- 删除“日期”标签。
- 大值 `WidgetDateValue`：`(12, 28, width, 56)`，如 `19`。
- 说明 `Body`：`(12, 96, width, 20)`，内容 `M月d日 · 周X`（例 `10月19日 · 周一`）。
- `ShowWeekday=false` 时只显示 `M月d日`。
- 年份只在非今年时加 `yyyy年` 前缀；其余情况不显示年份。
- 长格式/短格式仍尊重原有设置（`DateFormat`/`WeekdayFormat`）。

Medium 卡：大值 + 右侧说明（同上信息，不堆 4 层）。

实现：在 `BuiltInWidgetProviders` 的 `TimeProvider`/`DateProvider` 里改 `WidgetElement` 列表；新增私有 `ZoneCaption(TimeZoneInfo)`（用 offset 拼 `UTC+08:00`，负号处理）。

#### 3.2 卡片外观与工具栏

- `WidgetBoardRenderer.cs`：
  - `Radius = 16`（卡片）；`Rim = .75`；
  - 卡片面 `tone`：浅色 `0x14FFFFFF`（约 8%）、深色 `0x12000000`；卡边框/rim 用 `0x42FFFFFF` 上/左、`0x14FFFFFF` 下/右；
  - 工具栏：标题 `我的组件` (24,62,220,24)，16/24 semibold；按钮 `编辑`（ghost）与 `添加`（accent 胶囊，30 高）放在右侧 y=58；
  - 空态：垂直居中（约 y=150）：32 浅槽圆 + “还没有组件”16/24 + “添加时间或日期开始”12/16 + accent 按钮；
  - 添加菜单：右缘对齐“添加”按钮右缘；浅色浮层 `0xF8FAFC`、圆角 12、行高 44、图标 + 文案；
  - 编辑态：卡片加深 + 1px accent 边；底部 3 个 44 图标按钮（配置/大小/移除），右上拖动柄；不要 2×2 四按钮压满小卡；
  - 卡片按压状态：用 r16 专属状态层，不要通用 `min(w,h)/2` 圆角（当前会变椭圆暗罩，`WidgetScene.cs:1077-1082`）。
- 导航下那行长提示（“小组件 · 时钟（预览）”）删除；页面名由 Dock 选中项承担。

#### 3.3 验收

- 小组件页：两张小卡 + 一张宽卡；时间卡只显示 `02:23` + `UTC+08:00`；日期卡只显示 `19` + `10月19日 · 周一`；不再有多余标签/年份（今年）。
- 添加/编辑按钮可见、可点；菜单对齐；空态截图；编辑态截图；卡片不压 Dock。
- 构建 + Core tests（更新文本断言）。

---

### Phase 4 · 可访问性、测试、收尾

1. **UIA/键盘**：
   - 设置窗口 5 个分类 ←/→、Enter；Esc 关闭；焦点环可见。
   - Dock 5 项 + 首页把手：Tab 可达；名称含“预览”；Enter 激活；首页名称“回首页（陪伴）”。
   - 高对比模式：所有新控件用系统色；截图/代码检查。
2. **减少动态效果**：
   - Dock 选中/悬停、首页悬停：立即完成；无动画残留 timer。
3. **测试**：
   - 全量 Core tests；更新/新增：`DetailsChromeLayout` 矩形、`DetailsDockItems` 5 项、`WidgetLayoutMetrics` 详情 476、`WidgetBoardLayout` top=104、内置卡片快照文本、`SetDetailsPageHeight(512)`。
   - `dotnet build` 全解决方案；`tests/GlassDesk.PlatformChecks`、`NativeBridgeChecks` 在改动涉及 ABI 时跑（本计划不改 native，可只跑 Core + build）。
4. **文档**：
   - 更新 `docs/ui-plan-v2-2026-10-19.md` 状态为“已实施（日期）”。
   - 新增 `docs/ui-implementation-log-<date>.md`：每阶段改了什么、截图路径、未验证项。
5. **最终交付**：
   - 4 个阶段 commit；`git status` 干净。
   - 新 staging 运行包（`artifacts/qa/ui-final/staging`）；
   - 截图：空态/音乐/小组件/设置外观/设置陪伴/设置插件/编辑态/添加菜单；存 `artifacts/qa/ui-final/screens`，**不要提交**。
   - 明确列出未验证项：混合 DPI、100/200%、Present、真实网易云、驻留、安装器。

---

## 5. 逐文件改动清单（速查）

### Core
- `ContentPolicy.cs`：不变（Details 360×300/476）。
- `WidgetController.cs`：删除 `hasTimeline?512`；详情音乐恒 476。
- 新增 `DetailsChromeLayout.cs`：常量 + 矩形 helper。
- `UiPreviewContracts.cs`：新增 `DetailsDockEntry` / `DetailsDockItems`（5 项）。
- `Contracts.cs`：`WidgetActions.DockFirst=120`（追加）。
- `WidgetBoardBuiltIns.cs`：时间/日期元素 2 层；`ZoneCaption`；年份规则。
- `WidgetBoardData.cs`：`WidgetBoardLayout` 如需可把 `Top` 改 104（与 renderer 统一）。
- 测试：`tests/GlassDesk.Core.Tests/*` 更新相关断言。

### App
- 新增 `DetailsChromeVisual.cs`（把手 + Dock）。
- 新增 `UiRadius.cs`（圆角常量）。
- `WidgetScene.cs`：
  - 字段 `_navigation`→`_chrome`；所有调用点（见 2.3 表）；
  - `ArtworkPoseFor`、`UpdateMetadata`、`DetailsTransportY`、时间轴 offset、`DrawDetails`、`DrawDetailsBody`、`BuildControls`、`DetailsBodyViewportHeight`、`_detailsBody.Size`、`_detailsViewport.Offset`、受保护区域。
- `WidgetScene.Board.cs`：`_boardCards.Offset/Size`、`ScrollBoard`、`ApplyBoardVisibility`、`BoardNaturalHeight`。
- `WidgetBoardRenderer.cs`：`CardsTop/BottomInset/Radius/Rim`、工具栏、空态、菜单、编辑态、卡片面。
- `SettingsWindow.cs`：圆角/材质/去噪/InfoBar/ColorPicker 包裹/裁切/按钮状态/隐藏组件尺寸/页脚。
- `SettingsFloatingNavigation.cs`：外框 24、选中 20、hover、accent 选中、焦点环。
- `SettingsAcrylicBackdrop.cs`：固定 0.86，不跟滑杆。
- `WidgetInstanceConfigurationWindow.cs`：圆角/控件对齐（不改行为）。
- `GlassDeskCoordinator.Widgets.cs`：小组件页高度 512。
- `DetailsNavigationVisual.cs`：迁移完成后删除（或保留但不再引用；删前跑全量测试）。

### 不要改
`native/GlassDesk.Native/*`、`src/GlassDesk.Platform/*`、`src/GlassDesk.NativeBridge/*`（除非编译必须）、插件 SDK、发布脚本/安装器、正式配置目录。

---

## 6. 验证清单（每阶段都跑）

```powershell
# 1. 构建
dotnet build tests/GlassDesk.Core.Tests/GlassDesk.Core.Tests.csproj -c Release -m:1

# 2. 测试
dotnet run --project ./tests/GlassDesk.Core.Tests/GlassDesk.Core.Tests.csproj -c Release --no-restore -m:1

# 3. 发布 staging（见 1.4）
# 4. 启动 + 截图（见 1.5）
# 5. 人工检查：截图与 docs/design/preview/details-layout-options.png / ui-simple-onepager.png 对比
```

每阶段完成必须：
- `git status` 只含本阶段相关文件；
- commit 信息示例：`ui(phase1): settings cleanup + radius scale`；
- 截图命名：`phase1-settings-appearance.png`、`phase2-details-music.png` 等（不入库）。

---

## 7. 风险与回滚

| 风险 | 处理 |
|---|---|
| 移动导航破坏受保护几何/测试 | 保护项列表见 3.4；只更新经本计划批准的断言；删除旧导航前跑全量测试 |
| 详情页高度改 476/512 在矮屏越界 | `WidgetController` 已有 work area clamp；截图确认；异常则限制 Dock 以上内容并允许滚动 |
| Dock 点击与“外部点击收起”冲突 | Dock/把手命中优先于外部观察；`Controller.HandlePointer` 的 Close 只对首页把手；Dock 动作走 `RouteSpecialPointer` |
| ColorPicker 包裹后仍方角 | 用 `Border` + `Clip` 并监听 SizeChanged；截图验证 |
| 材质改动回退透明度 bug | 不改 `HostFrame.Solid`、不改 `TintOpacity` 的组件逻辑；设置窗口只改 acrylic 常量 |
| 预览项被误认为真实页面 | 名称带“预览”；点击不切页；不做假页面 |
| 无实机验证导致偏差 | 每阶段截图给用户；未验证项写进 implementation log |

回滚：
```powershell
git checkout <上一 commit> -- src/GlassDesk.App/SettingsWindow.cs   # 单文件
git revert <commit>                                                 # 整阶段
```

---

## 8. 参考链接（执行时可核对）

- iOS Tab Bar（底部导航、2–5 项、图标+文字、选中着色）：
  https://developer.apple.com/design/human-interface-guidelines/tab-bars
- Material 3 Navigation Bar（3–5 目的地、选中胶囊指示器）：
  https://m3.material.io/components/navigation-bar
- macOS Dock（悬停出名字、选中项浮起、半透明）：
  https://developer.apple.com/design/human-interface-guidelines/dock
- Apple 同心圆角（嵌套圆角共享圆心）：
  https://developer.apple.com/documentation/swiftui/roundedrectangularshapecorners/concentric(minimum:)
- Windows 11 Geometry（同层一致、不同尺寸不同半径）：
  https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/geometry
- Material 3 形状阶梯（4/8/12/16/28/full）：
  https://m3.material.io/styles/shape/corner-radius-scale
- 设计技能里的同心圆角：
  https://github.com/pproenca/dot-skills/blob/master/skills/.experimental/adversarial-ios-design/references/layout-concentric-corners.md

项目内参考图：
- `docs/design/preview/details-layout-options.png`（最终布局对比：顶部把手 + Dock / Tab Bar）
- `docs/design/preview/ui-simple-onepager.png`（圆角、设置、卡片）
- `docs/design/details-layout-options.html`、`docs/design/ui-simple-onepager.html`（可点链接）
- 真实截图：`artifacts/qa/ui-look-2026-10-19/screens/`（设置/胶囊/空态；不入库）
- 历史证据：`artifacts/qa/typography-widgets-preview2/`、`artifacts/qa/ui-0.3.0-preview.1/`

---

## 9. 附录 A：坐标 / 常量速查

```
卡片宽 360
首页把手  58×28，top14，r14，居中
Dock      320×48，bottom16，r24，居中；5 项中心 x=32/96/160/224/288（Dock 内）
选中胶囊  88×36，r18
悬停牌    20 高，r10，11–12 DIP 字
音乐页    360×476
  封面(96,70,168,168,r16) 标题(24,246,312,24) 歌手(24,274,312,20)
  时间轴(56,300,248,38) 控制 上一首(96,346,44)/播放(152,340,56)/下一首(220,346,44)
  Dock(20,412)
小组件页  360×512
  标题(24,62,220,24) 按钮 y58 卡片(24,104) 小卡150×148 中卡312×148 gap12 Dock(20,448)
空态      360×300
  图标(158,62,44) 文案(100,118,160,24) 按钮(100,162,160,36) Dock(20,236)
```

### 附录 B：真实指针点击（稳定展开/点 Dock）

```powershell
Add-Type @"
using System;using System.Runtime.InteropServices;
public class M {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out P p);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 public struct P { public int X,Y; }
}
"@
$p=New-Object M+P; [void][M]::GetCursorPos([ref]$p)
# 胶囊中心（window rect + 半宽/半高）；示例坐标需按实际窗口 rect 计算
[M]::SetCursorPos($x,$y); Start-Sleep -Milliseconds 200
[M]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80
[M]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
# 操作完还原
[M]::SetCursorPos($p.X,$p.Y)
```

> 这是“人工点击”的等价操作，会短暂移动用户鼠标；测试前告知用户，操作完还原。窗口 rect 用 1.5 的 DPI 换算（150%）。

## 10. 附录 C：默认决策（用户已同意“差不多”）

1. 底部导航用**方案 1 浮动 Dock**（不是全宽 Tab Bar）。
2. 圆角 **24/16/10/全圆**。
3. 设置保持**两列**，窗口尺寸 880×680 不变。
4. 顶部把手点击 = 回时间胶囊；悬停名字“回首页”。
5. Dock 5 项 = 音乐、小组件、时钟、专注、随记；后三项标“预览”，点击不切页。
6. 右上圆按钮删除。
7. 用户草图中“底部小圆”未解释，本轮**不做**；如用户后续说明再补。
8. 不做深色主题、不改 native、不改插件 SDK、不加功能页。

**执行时如果发现与本文档冲突的代码，以本文档为准；如果实现代价明显超出（例如 Dock 动画需要新增复杂基础设施），先做“静态可用版本”并截图给用户确认，再决定是否加动画。**

---

## 11. 新会话开场提示词（直接复制）

```text
你在 Windows 项目 GlassDesk 里执行 UI 改造。项目根：
D:\Believe_in_yourself\about AI\work about agents\共同工作区\GlassDesk

先完整阅读并严格按 docs/implementation-plan-final-2026-10-19.md 执行。
这是用户已确认的最终计划，不要重新讨论方向、不要缩减功能、不要改 native。

硬性要求：
1. 先 git status 确认干净，git checkout -b ui/final。
2. 按 Phase 1 → 2 → 3 → 4 顺序做，每阶段单独 commit；每阶段先构建 + Core 测试，再 publish 到新 staging，启动预览截图给用户看，用户点头后再进下一阶段。
3. 保护项：时间白板 (68,14,184,32)、8 根频谱、陪伴液体运动、播放/暂停动画、胶囊/小展开、耳朵/角、插件 API、正式用户配置。不要改 native/(除非编译必须)。
4. 右上圆按钮删除；顶部陪伴=首页把手（点击回胶囊）；底部 5 项 Dock（音乐/小组件/时钟/专注/随记）；圆角 24/16/10/全圆；设置保持两列、只去噪。
5. 没有 computer use 也能验收：用计划 1.5 节的 PowerShell 启动 + DPI-aware 抓窗口；截图存 artifacts/qa/ui-final/screens/，不要提交带用户桌面的截图。
6. 每阶段完成后，在 docs/ui-implementation-log-<日期>.md 记录：改动文件、截图路径、未验证项。
7. 如果计划与代码冲突，以计划为准；如果某个实现代价明显超出，先做静态可用版本并截图给用户确认，再决定是否补动画。
```

