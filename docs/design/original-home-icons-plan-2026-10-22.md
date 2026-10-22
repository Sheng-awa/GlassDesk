# GlassDesk 原创首页图标：网易云音乐 / Edge 执行计划

日期：2026-10-22，V2 简约修订。状态：**后续执行计划，本轮未生图、未替换图标**。

用户已否定上一版的复杂阴影与抽象隐喻。本版完整替换原来的“唱片实体 / 指南针 / 磨砂表层”方案：**平面、无阴影、音乐音符 / 浏览器窗口，一眼先认出用途。** 不要再混用旧提示词。

这份文件可独立交给下一位 agent。无需重读聊天，也不要把下面的美术建议当成用户已经确认的配色方案。

## 1. 已确认的范围与边界

用户要求重新设计首页入口的图标，首先只做网易云音乐与 Edge。图标要原创，不要求沿用软件原来的标志，也不做跨软件适配。入口仍分别打开原来的应用，名称仍为“网易云音乐”和“Edge”。

本轮不改 Terminal、照片、记事本、计算器；不改设置 UI；不改 Dock 和导航卡配色。后两项已记录为后续设计议题，目前没有用户选定的色板。不能为了匹配新图标擅自重做整页。

保持应用图标的统一槽位与当前点击目标。图形使用自己的纯色底面，不要在应用图标外面再套玻璃卡片或装饰框。陪伴球、导航卡和文件夹仍是其他独立元素，不要混入图标图案。

## 2. 当前问题、依据与本次的设计方向

依据用户最后提供的首页截图：网易云音乐和 Edge 的真实图标占满图标槽，Terminal、照片等系统资源内部留白较多，看起来更小。这是**可见图形面积不一致**，不能只靠统一 Image 宽高修复。

当前 `HomeVisual.cs` 优先画 `HomeIconLoader.cs` 读取的本机图标，缺失时画彩色底面和系统 glyph。两条来源的笔画、明暗、内边距、体积感都不同。后续先用两枚原创图标建立标尺，而不是声称只替换两枚就能消除全页差异。

上一版提示词同时要求实体、唱片凹面、玻璃表层、光线与内阴影，生成器容易把它们全部画出来；浏览器又叠了窗口和指南针，缩小后可能先被认成指南针工具。用户最新反馈正是“阴影又多又复杂”“一眼不知道是什么”。这些是本次改方向的依据，并非已完成的用户识别测试。

新方向：**一个纯色圆角底面 + 一个粗而清楚的白色用途符号**。音乐用连在一起的双音符；浏览器用简化窗口内的地球。两枚都为正视平面图形，不加光源、厚度、凹面、磨砂、反射、渐变、阴影或装饰框。入口名称继续明确具体应用；图案负责表达“音乐 / 浏览网页”。

这是供用户评审的美术提案，既不照搬 macOS 图标，也不把 Material / Fluent 的外观拼成一套。借 Apple 的克制、Google 的识别性、Windows 的小尺寸清晰度。

## 3. 参考网页及其具体作用

| 参考 | 这次看什么 / 产出什么 | 不用于什么 |
|---|---|---|
| [Windows app icon design](https://learn.microsoft.com/en-us/windows/apps/design/iconography/app-icon-design) | 单一隐喻、轮廓、光学面积、小尺寸简化；为两枚图标做尺寸与轮廓检查 | 不把系统图标图案当作原创资产 |
| [Apple App icons](https://developer.apple.com/design/human-interface-guidelines/app-icons) | 研究单一中心主体、底面比例与缩小后的识别性 | 不照搬平台图层、玻璃和厚度，不导入 Apple 图标 |
| [Material 3 Icons](https://m3.material.io/styles/icons/overview) | 对照同一组符号的视觉重量和清晰度；检查两枚图标是否像一套 | 不用现成符号替代本次要求的生图 |
| [BrimDeck MusicVisuals 源码](https://github.com/MoFeng2223/BrimDeck/blob/e3765b1b51c5f3fb7d05feee7c1f6f4ba082eff3/src/BrimDeck/MusicVisuals.cs) | 区分“应用入口图标”和“播放控制符号”；后者应该稳定、清楚 | 不把播放按钮或第三方品牌图形画成入口图标 |

核实说明：2026-10-22 已读取 Windows 指南正文；Apple 与 Material 的网页在当前搜索工具中需要 JavaScript，列为后续视觉参考，不能声称已在这次工具里完整看过全部动态示例。上述网站的具体像素规范不自动成为 GlassDesk 的硬性规范。下面的数值是本项目建议值。

原有研究文档 `GlassDesk_UI_Design_Research.md` 中，Apple / Material / Fluent 三者各自承担不同职责的研究方法继续适用。这里已写出本任务需要的结论，不要求执行者重读整份文档。

## 4. 两枚图标共用的美术规格（待样张评审）

| 项目 | 执行要求 |
|---|---|
| 构图 | 一张图只含一枚图标，居中，正视，不出现设备、应用界面或场景 |
| 母版 | 目标 1024×1024 PNG，真实透明背景；工具不支持指定尺寸时，保留原始输出并另行导出目标尺寸 |
| 画面边距 | 主体外轮廓约占画布 82–86%；两枚先按面积校准，再按观感微调 |
| 视角 | 正视的平面 2D 图形，无透视和厚度 |
| 光线 / 材质 | 不指定光源，不模拟物体；纯色填充，无高光、反射、磨砂、玻璃、凹凸和渐变 |
| 阴影 | 内阴影、投影、接触阴影、环境阴影、光晕全部禁止；接入时也不新增图标阴影 |
| 符号 | 音乐：一组粗白色双音符；浏览器：粗白色窗口轮廓内一个简化地球，地球只有外圆、一条经线弧与一条赤道。无地址栏文字、工具栏小点和其他装饰 |
| 色彩提案 | 音乐底色珊瑚红 `#EF4560`；浏览器底色蓝 `#1689D8`；符号白色。每枚只用底色与白色两种实色，透明背景不计；配色供样张评审，不改变整页 accent |
| 笔画 / 留白 | 白色主符号占底面宽度约 55–62%；32 px 缩图仍能分清负形，不依赖细线；两枚的底面尺寸、圆角和符号视觉重量一致 |
| 内容限制 | 无文字、字母、数字、水印、品牌 logo、陪伴眼睛、包装 mockup、背景平面 |
| 名称 | 图标下面仍显示应用名，用于消除原创隐喻的学习成本 |

两枚最终的小尺寸可见面积目标相差不超过约 5%，这个数值只作初筛；圆形和矩形不能只按 bounding box 机械等大。允许在同一个 52 DIP 槽位内部微调图形尺寸，不改网格。

## 5. 生图提示词：可直接执行

执行时使用当时可用的生图工具，**透明背景参数设为 true**。这两次是新图生成，不传软件原 logo 作为编辑目标。不要用 Python、SVG 或截图拼装替代本次生图；后处理可以导出尺寸和检查 alpha，但改变图案、材质或去背景应回到生图编辑工具。

先各生成一个候选，作为同一张对照页评审；不要先批量生成十几枚。若一枚风格偏离，以上一枚已认可的原创图作为“仅风格参考”重做，不让参考图的主体混入另一枚。

提示词故意不使用 premium、physical object、satin、frosted、depth、soft lighting 等容易触发立体材质的描述。用途符号是通用语义，形状与构图自行设计，不复制应用品牌标志。

### 5.1 网易云音乐入口：纯色底面与双音符

```text
One minimal flat 2D music launcher icon. A solid coral-red (#EF4560) rounded square containing one bold white pair of joined eighth notes. Use large simple shapes, balanced spacing and a clear music silhouette recognizable at 32 pixels. Original note geometry, no brand logo.

Front view. Centered icon occupying 84% of a square canvas. White symbol approximately 60% of the colored square width. Two solid colors only; transparent outside the square, genuine alpha. Target 1024×1024 PNG.

No shadows of any kind, gradients, highlights, glass, texture, bevels, depth, discs, rings, text, extra frames, scenery or watermark.
```

缩小后应先看见“音乐音符”，不能像靶心、电源键或装饰物。若生成网易云原标志，整枚弃用重生成；若仍有任何阴影或渐变，要求删除，不用“减弱一点”保留。

### 5.2 Edge 入口：纯色底面与浏览器窗口

```text
One minimal flat 2D web-browser launcher icon. A solid blue (#1689D8) rounded square containing one bold white browser-window outline. Inside the window, one simple globe: an outer circle, one meridian curve and one equator. Large clear strokes, readable at 32 pixels. No toolbar buttons or tiny details. Original geometry, no Edge logo.

Front view. Centered icon occupying 84% of a square canvas. Main symbol approximately 60% of the colored square width. Two solid colors only; transparent outside the square, genuine alpha. Target 1024×1024 PNG.

No shadows, gradients, highlights, glass, texture, bevels, depth, compass, swirl, letters, text, extra decorative frames, scenery or watermark.
```

若 32 px 时窗口内的地球挤成白团，先扩大窗口与地球之间的负形，再减少线条；仍不清楚时只保留大地球作为第二候选，不叠更多符号。不要重新引入指南针。最终只选一枚，不同时接入两套。

### 5.3 两图风格统一的编辑提示词模板

```text
Keep the target icon's subject and colors. Match the approved reference only in flat 2D construction, rounded-square size, corner radius, bold white stroke weight and balanced margins. Use solid fills. Remove every shadow, gradient, highlight, texture and depth effect. Do not import the reference's subject or logo. Preserve genuine transparent background. Return one icon only, no text or scene. Keep it recognizable at 32 pixels.
```

编辑前必须分别标清“目标图”和“风格参考图”。需要重新附图时应明确索取，不能拿不相关的聊天截图代替。

## 6. 交付文件与接入步骤

1. 将原始母版、最终选中图与实际提示词保存到项目，例如 `artifacts/design/home-icons/2026-10-22/`，不要只留在临时工具目录。
2. 先产出评审页：两图各按 32 / 52 / 78 / 104 px 显示，放在白色、淡灰、深色与一张高明度壁纸样本上。不用整页重新渲染；不得把母版缩略图的美观误当成小尺寸已达标。
3. 用户选定后再将最终 PNG 放到 `src/GlassDesk.App/Assets/HomeIcons/`；命名建议 `netease-original-v1.png` 和 `edge-original-v1.png`。保留 alpha。额外导出 52 / 78 / 104 / 156 px 版本是否必要，由实际渲染锐度决定，不先建复杂资源系统。
4. 修改 Home 本地加载的优先级：这两个入口优先加载随包原创资源，缺失或损坏时退回现有本机图标/原 fallback。其他四个入口的逻辑保持原样。
5. 不让异步 `HomeIconLoader` 稍后覆盖已加载的原创图：在请求和结果应用两端都明确两枚资源的来源优先级。不要通过延迟几秒加载掩盖竞态。
6. 沿用 `HomeVisual.SetAppIcon`、现有缓存 revision 和 SurfaceOp.Image 路径。资源 decode 不放入鼠标 Move 或动画帧；图片更新只触发一次重绘。
7. 若要添加图片文件复制项，在 `.csproj` 中显式保证 Build 与 Publish 都带上资源；路径基于应用目录，不依赖启动 WorkingDirectory。不得只在开发机的绝对路径读取。
8. 首页标签、点击目标、UIA 名称、启动命令均不变。只替换视觉资产，不增加第三方软件账号/网络接口，也不做客户端适配。
9. 输出一份简短修改记录：提示词版本、资产路径、采用的样张、已知问题；再做一次普通编译和用户试用包。

## 7. 轻量验收（用户先试）

- 两枚在 52 DIP 同一行里看起来相同重量；不能一枚填满而另一枚缩成小 logo。
- 先隐藏标签：32 px 应可分辨音乐与浏览网页这两类用途，不能只认出一个红块和蓝块；这是待用户评审项，不能用生成器自己的描述当作已通过。
- 两枚均无内外阴影、渐变、玻璃与高光；52 px 不依赖细纹。透明边缘没有白框、黑边或棋盘格。
- 白色材质上可读，深色背景不丢暗边。只对图标本体评审，不顺便调 Dock / 导航色。
- 原创图案与现有品牌标志有明显区别；用户仍能从文字知道打开哪个应用。
- 悬停/按下继续使用当前整体缩放，不再叠新的光晕或循环动画。
- 发布目录缺失一枚资源时，应用保留正常入口与 fallback；不因图片失败阻止打开应用。
- 本轮不要求录屏、复杂性能测试或自动化全流程。最终观感由用户试用决定。

## 8. 记录但不在这次图标任务实施的项目

| 项目 | 状态 / 后续要求 |
|---|---|
| 其他四枚首页图标 | 暂不做；以后再沿同一美术规范统一 |
| Dock 配色 / 导航卡配色 | 用户尚未想好；保留当前实现，单独出色板样张后决定 |
| BrimDeck 设置 UI | 仅作未来参考，见下方入口。保留 GlassDesk 当前设置 IA、浅色主题、蓝色 accent |
| 网易云“喜欢” | 研究已确认依赖完整控制；不能伪装为 SMTC 已支持。需要单独决定是否引入可选增强连接，不能与图标换皮绑定 |

## 9. BrimDeck 研究交接：哪些能拿、哪些不能只搬一个按钮

参考版本：`e3765b1b51c5f3fb7d05feee7c1f6f4ba082eff3`，Apache-2.0。此次只移植了上一首/下一首的矢量轮廓，修改为 WUC 绘制；原始许可与变更说明在 `licenses/BrimDeck/`。

- [播放器支持说明](https://github.com/MoFeng2223/BrimDeck/blob/e3765b1b51c5f3fb7d05feee7c1f6f4ba082eff3/docs/zh-CN/music-players.md)：明确普通 SMTC 不提供“喜欢”，完整控制需要重启客户端；适合判断功能可行性。
- [NeteaseSource.cs](https://github.com/MoFeng2223/BrimDeck/blob/e3765b1b51c5f3fb7d05feee7c1f6f4ba082eff3/src/BrimDeck/Native/NeteaseSource.cs)：看进程识别、显式启用增强连接、失败回退；不能只复制固定端口数字。
- [NeteaseCdpConnection.cs](https://github.com/MoFeng2223/BrimDeck/blob/e3765b1b51c5f3fb7d05feee7c1f6f4ba082eff3/src/BrimDeck/Native/NeteaseCdpConnection.cs)：看端口进程归属校验、连接生命周期、超时与状态消息；它不是 WinUI 控件库。
- [NeteaseBridgeScript.cs](https://github.com/MoFeng2223/BrimDeck/blob/e3765b1b51c5f3fb7d05feee7c1f6f4ba082eff3/src/BrimDeck/Native/NeteaseBridgeScript.cs)：`canLike/liked` 来自已登录且在线客户端的内部状态；歌曲身份匹配后派发喜欢命令，结果要通过状态回读确认。不能在鼠标点击时直接把红心点亮。
- [设置相关源码目录](https://github.com/MoFeng2223/BrimDeck/tree/e3765b1b51c5f3fb7d05feee7c1f6f4ba082eff3/src/BrimDeck)：以后看 Settings 开头的页面、分组、控件和空状态。它是 WPF，实现方式不能原封照搬 WinUI。用户本轮只要求记下参考，未要求改设置。

若下一轮要实现客户端收藏，单独交付：可选启用入口与明确说明、禁用/断开回退、歌曲身份绑定、`CanLike/IsLiked/Busy` 状态、结果确认、超时错误、客户端普通重启后恢复 SMTC。GlassDesk 的离线插件模式不因此改成默认联网；收藏必须尊重网易云客户端本身的登录/网络状态。不得用“GlassDesk 本地收藏”冒充网易云账号收藏。
