<proposed_plan>
# GlassDesk 0.3.0-preview.2：统一排版与材质层次、补充导航语义、增加离线小组件页

0. 执行约定与已确认决策
本计划供其他会话中的工程师或 AI agent 执行。本会话只进行规划，未修改源码、生成新样张或运行新版本验收。
唯一项目目录：
[GlassDesk](D:/Believe_in_yourself/about AI/work about agents/codeX/codeX work/GlassDesk)
本计划以 2026-10-17 的当前 0.3.0-preview.1 实现为基线，不再按早期截图或旧计划重做界面。现有文档记录的 271 项检查属于历史结果，不能直接用作本轮通过证明；尚未完成的驻留、混合 DPI、完整 Present 帧率等项目继续如实标记。
已锁定的产品决策
项目	本轮决定
字体	产品默认采用 A：Segoe UI Variable＋系统 CJK；先交付 200% 中拉文对照样张
字体 B	仅作为对照和条件性备选，优先 Noto Sans SC；不得自动替换产品字体
大展开真实页面	只有“音乐”“小组件”两页
原有五项导航	保留身体、耳朵、眼睛、细条、几何、环绕和弹簧表现
小组件入口	优先采用胶囊右端卫星入口，不新增第六槽
导航改动	补名称提示、可见选择指示、语义视线及正确的可访问信息
小组件首版	离线时间、日期及可调整的本地插件卡片
插件降级	新插件卡片贡献未通过稳定性验收时，交付时间／日期卡片，暂不开放第三方卡片贡献
天气	下一轮单独实现，本轮不联网获取天气
页面记忆	关闭、隐藏及再次展开恢复最后真实页面；默认同时支持重启后恢复
设置	IA、布局、浅色主题、蓝色 accent 保持
强调色	固定保留有效蓝色 #1689F9，不跟随陪伴色
版本与交付	新建 0.3.0-preview.2 预览输出，不覆盖此前发布包和配置


P1 保护清单
以下保护项优先于视觉统一：
1. 时间白板布局：当前位于音乐小展开中，默认 184×32 DIP，位置为 (68,14)；保留既有日期／星期打开时的高度规则。不得误把整个桌面窗口改成 184×32。
2. 频谱：保留 8 根短条、静止高度、宽度、间距、锚点和各形态位置计算。
3. 设置：保留五个分类及顺序、顶部导航、主体结构、列布局、控件排列、浅色主题与蓝色强调色。
4. 陪伴导航：不重画黑胶囊身体，不改五槽几何，不替换现有耳朵／恶魔角／眼睛和弹簧。
5. 媒体规则：暂停五秒只影响胶囊内容，不能切换真实页面或关闭大展开。
“P1 零改动”指保护参数、行为和结构零变化。授权的文字字形、墨色及基线校准属于本轮排版工作，不能要求这些文字像素同时保持原样。
A. 现状诊断：为什么仍显得“平”
诊断必须区分“代码已证明”“截图可观察”“需要样张验证”，不把待验证推测写成确定原因。
A1. 证据目录
主要源码：
- [原生文字绘制 (line 507)](D:/Believe_in_yourself/about AI/work about agents/codeX/codeX work/GlassDesk/native/GlassDesk.Native/GlassDeskNative.cpp:507)
- [组件排版与形变 (line 1146)](D:/Believe_in_yourself/about AI/work about agents/codeX/codeX work/GlassDesk/src/GlassDesk.App/WidgetScene.cs:1146)
- [设置文字和颜色 (line 26)](D:/Believe_in_yourself/about AI/work about agents/codeX/codeX work/GlassDesk/src/GlassDesk.App/SettingsWindow.cs:26)
- [现有绘制命令 (line 6)](D:/Believe_in_yourself/about AI/work about agents/codeX/codeX work/GlassDesk/src/GlassDesk.NativeBridge/SurfaceOp.cs:6)
- [原生 ABI (line 20)](D:/Believe_in_yourself/about AI/work about agents/codeX/codeX work/GlassDesk/native/GlassDesk.Native/NativeApi.h:20)
已查看的近期截图：
- [设置外观页](D:/Believe_in_yourself/about AI/work about agents/codeX/codeX work/GlassDesk/artifacts/qa/ui-0.3.0-preview.1/current-playing-appearance.png)
- [大展开浅背景](D:/Believe_in_yourself/about AI/work about agents/codeX/codeX work/GlassDesk/artifacts/qa/ui-0.3.0-preview.1/resume-glass-light-details-crop.png)
- [小展开](D:/Believe_in_yourself/about AI/work about agents/codeX/codeX work/GlassDesk/artifacts/qa/ui-0.3.0-preview.1/resume-reduced-peek-crop.png)
这些是已有版本证据，不是本轮新效果图。窗口截图可能遗漏系统 Backdrop，因此不能仅凭截图中的灰底推断实际材质不透明度。
A2. 逐项诊断
问题	当前证据	本轮处理
字号层级压缩	音乐标题／歌手为胶囊 13/11、小展开 16/12、大展开 17/12。大展开标题仅比小展开增加 1 DIP	按角色建立尺度，大展开标题提升至明确的标题层级
字重表达有限	SurfaceOp.Bold 是布尔值；原生只映射 400／600	增加数值字重，不再用一个布尔值表达所有强调
中拉文重量存在错配风险	原生固定拉文字族并使用默认回退，没有记录 CJK 实际匹配字重；截图中混排重量不完全一致	输出实际字体／字重记录，做 200% 样张；不靠描边补重量
墨色档位不足	设置 InkColor 与 SecondaryColor 均为 #20252C	建立四档语义墨色，区分标题、正文、说明和禁用信息
光学排版缺失	原生固定使用 Segoe UI Variable Text，没有 Display／Text 分工及显式 tracking	加入光学角色、行高、基线和按 run 应用的字距
测量与绘制可能不一致	音乐胶囊宽度使用 XAML TextBlock 测量，实际由原生 DWrite 绘制	两者使用同一原生测量入口、字体策略和 token
等宽数字覆盖不完整	只有“长度为 5、第三字符为冒号、其余为数字”的文本被加上 tnum	改为语义标记，覆盖时间、时长和数值字段
材质层次不连续	已有按钮、滑块和频谱局部深度，但文字与承载面之间缺少统一规则	保留既有有效深度，补内容承载层和文字层关系
形变缺少相对运动	主内容随相同几何进度插值，缺少独立的延迟及位移层	对非保护区内容增加一次性、低幅度视差
多套尺度分别维护	原生组件、设置和导航各自硬编码字号、颜色和字重	引入一套共享 token，映射到原生和 XAML


A3. 不成立的诊断与禁止的误修
- 当前已经使用灰阶文字抗锯齿，不将问题归咎于“没开 ClearType”，也不在透明表面强行开启 ClearType。
- 当前已有局部控件深度，不推倒重做全部按钮。
- 184×32 时间白板和 8 根静息条属于设计，不作为缺陷。
- 不把设置蓝色覆盖成应用级旧青绿色资源。当前控件生效色来自设置局部资源 #1689F9。
- 不通过增大外壳阴影来代替内部层级改造。
B. 设计原则与可检验判据
方向收敛为：Apple 的清晰材质分层，加上 Material 的语义排版和 tonal 层级，使用适合 Windows 的字体与输入方式。
Apple 将材质用于区分前景文字／控件和背景；Material 用文字角色组织信息。这些原则用于本项目的系统设计，不复制其品牌资产或完整组件外观。Apple Materials、Material Typography
B1. “字浮在材质上”的六个判据
判据	实施要求	验收方法
层级落差	每个区域只有一个最高文字层级，标题、值、正文、说明可区分	灰阶截图仍能识别主次
光学排版	使用合适的光学字族、基线、行高及中拉文重量	200% 样张与字体记录
墨色层级	四档语义墨色，重要文字不靠低透明度退后	对比度表与实际材质截图
材质分层	背景、承载面、文字／操作层职责分开	静止画面也能读出前后关系
空间节奏	文字组内紧、组间松；按共同基线和中心轴排布	基线、间距和边界读回
运动视差	形变时部分内容相对外壳有短暂滞后，最终完全归位	连拍及动画参数记录


B2. 字体禁止项
- 文字描边、挤出、内阴影、发光。
- 重复偏移绘制文字来伪造粗体。
- 中文统一增加字距。
- 对整段中拉文同时施加负 tracking。
- 横向压缩或拉伸文字以塞入槽位。
- 以缩小字号作为长文本的首选处理。
- 为强调而普遍使用 700／900。
- 文字长期呼吸、浮动、滚动或随鼠标漂移。
- 形变过程中缩放整段正文。
- 打包 Apple SF／苹方或未经核实可分发的字体。
材料 rim、可访问焦点环和既有滑块轨道不属于文字描边，但不得演变成全界面发光边框。
B3. 对比门槛
- 启用状态的小号文字：至少 4.5:1。
- 大号文字：至少 3:1；本轮保守地仅将 ≥24 DIP 的文字按大字处理。
- 必要图标和操作状态边界：至少 3:1。
- 第四档低强调墨色仅用于禁用或非必要装饰，不承载唯一的信息。
- 对比度依据最终合成背景计算，不能只比较 ARGB 字面值。
2–6% tonal scrim 是层次手段，不能承诺解决任意壁纸上的对比问题。对比不足时先提升墨色档位；仍不满足时使用现有提高对比／降低透明度路径，不给文字加描边。
B4. 统一不等于所有状态一样大
胶囊、小展开、大展开和设置共享：
- 字体策略；
- 文字角色；
- 墨色角色；
- 行高和间距规则；
- 光向；
- 动画时序。
各状态保留适合其信息密度的字号和布局，不把设置字号机械套进胶囊。
C. Typography、Ink、Material 与 Motion Token 全表
C1. 字体 A／B
方案	字体组合	产品包体影响	许可与限制	本轮地位
A	Segoe UI Variable；简中系统 CJK；其他语言按系统回退	新增字体文件 0	使用系统字体，不复制 Windows 字体文件	默认产品方案
B	Noto Sans SC Variable，缺失脚本继续系统回退	当前源文件约 16.9 MB；实际 ZIP 增量必须实测	SIL OFL 1.1，附版权及许可	QA 对照、条件性产品备选
其他参考	MiSans Variable	本次未核实选定文件的精确大小	官方允许嵌入并要求注明使用，不能套用第三方脚本仓库的许可	不优先采用


Windows 官方说明 Segoe UI Variable 具备字重与光学尺寸能力，并提供非拉丁文字字体建议。Windows Typography
Noto 文件大小与许可依据：字体文件、OFL。MiSans 依据其官方 FAQ。
C2. 先做的 200% 样张
首阶段产出三组，统一文字、字号、背景及测量方式：
1. 现状基线：当前固定 Text 字族、400／600、现有回退。
2. A 优化版：系统字体＋本计划 token＋显式回退和光学校准。
3. B 对照版：QA 私有加载 Noto Sans SC，相同 token。
B 字体只放 QA 资源目录，不注册为系统字体，不因生成样张就进入产品包。
测试字符串至少包含：
时间 日期 设置 播放 暂停
今天 08:08 · GlassDesk
Windows 11 — 音乐
上海 / Shanghai
天球(そら)のMúsica
GPU 27% · 24°C
00:00  11:11  23:59
08:08:08  11:11:11
每组附：
- 请求字体和实际字体；
- 请求字重和实际 face 字重；
- 光学字族／轴；
- locale；
- 基线、行高、advance；
- 是否存在字体模拟；
- 100% 原图和 200% 渲染图。
200% 图必须以对应 DPI 重新渲染，不能把小截图直接插值放大后冒充字体样张。
C3. 字重与光学策略
字重语义：
语义	Latin 请求值	CJK 初始请求值
Regular	400	400
Medium	500	400
Semibold	600	500


CJK 系统字体不一定具有真实 500／600。必须记录实际匹配结果，不能把请求 500 写成“实际 500”。
样张阶段对比 CJK 可用 face 与 Latin 的笔画重量。若 A 在多个常用字号下仍明显不协调，提交 B 的同条件样张、许可清单和包体增量，再决定是否进入 B；不得用伪粗体补救。
光学角色：
角色	A 的优先策略
小字，≤12 DIP	Small 光学类别
普通文字，13–19 DIP	Text
标题和大数值，≥20 DIP	Display
图标	继续 Segoe Fluent Icons，不走正文光学策略


优先使用系统可用的 Variable／Small／Text／Display 能力，检查实际解析结果；不可用时按既定顺序回退。B 若没有 opsz，不得宣称它具备该轴。
C4. 字号、字重、字距、行高
tracking 单位统一为 em。
Token	字号／行高 DIP	字重语义	Latin tracking	CJK tracking	光学类别
clock.capsule	25／32	Regular	0	0	Display
clock.whiteplate	16／26	Regular	0	0	Text
clock.legacyAux	保留当前 10／12	Regular	0	0	Small
clock.peekHero	34／44	Regular	0	0	Display
media.capsuleTitle	14／20	Medium	0	0	Text
media.capsuleArtist	12／16	Regular	0	0	Small
media.peekTitle	18／26	Semibold	0	0	Text
media.peekArtist	13／18	Regular	0	0	Text
media.detailsTitle	20／28	Semibold	−0.01，仅纯 Latin 标题	0	Display
media.detailsArtist	14／20	Regular	0	0	Text
page.title	20／28	Semibold	−0.01，仅纯 Latin	0	Display
section.title	16／24	Semibold	0	0	Text
body	14／20	Regular	0	0	Text
label	14／20	Medium	0	0	Text
caption	12／16	Regular	0	0	Small
caption.settings	13／18	Regular	0	0	Text
navigation.settings	保留 13；目标行高 18	选中 Semibold，其他 Regular	0	0	Text
navigation.semantic	12／16	Medium	0	0	Small
widget.clock	36／44	Regular	0	0	Display
widget.dateValue	48／56	Regular	0	0	Display
widget.value	36／44	Regular	0	0	Display
widget.label	12／16	Medium	0	0	Small


约束：
- P1 文字仍使用原有槽位；clock.legacyAux 是保护区兼容 token，不推广到其他新内容。
- 设置仅修改排版属性和墨色，不通过改行列、padding 或重排控件来适应新字。
- 固定槽位中，实际行高不能超过槽位。设置原有自动测量行为须在基线对比中验证。
- 长标题先省略；可用完整名称通过辅助技术提供。
- 数字字段 tracking 始终为 0。
C5. 四种界面映射
界面	主层级	次层级	辅助层级	保护规则
胶囊	时间／曲名	歌手	现有紧凑辅助信息	不改变时钟规则和频谱锚点
小展开	曲名或时钟主体	歌手、时间	必要状态	白板几何及填充策略冻结
大展开音乐	曲名、封面	歌手	时长／页面语义	频谱位置冻结
大展开小组件	主要数值	日期／时区说明	组件标签	使用同一字体和墨色系统
设置	既有页标题／设置名称	控件值	说明文字	IA、布局、浅色、蓝色冻结


C6. 四档墨色
全部使用不透明文字色，不通过降低文字 alpha 模拟远近。
角色	浅材质上的深墨	深材质上的浅墨	用途
Ink 1	#20252C	#F8FAFC	标题、主要数值
Ink 2	#48515B	#DFE5EC	正文、歌手、设置值
Ink 3	#626C78	#B4C0CC	说明、标签、时区
Ink 4	#8E98A3	#84909C	禁用和非必要装饰


计算参考值：
深墨角色	白底	#F4F5F7	#D8DCE2
Ink 1	15.41	14.13	11.20
Ink 2	8.07	7.39	5.86
Ink 3	5.34	4.89	3.88
Ink 4	2.93	2.68	2.13


这是纯色数学参考，不是现有玻璃的实测结论。
处理规则：
- Ink 3 在较灰背景上不足 4.5:1 时提升为 Ink 2。
- Ink 4 不用于启用状态的小号必读文字。
- 设置保持浅色主题，不因桌面变暗而切成另一套深色设置。
- #1689F9 对白约为 3.51:1，继续用于现有开关、轨道和强调状态，不直接拿来替代小号必读正文。
C7. 材质与动效 token
Token	默认值
tone.content	中性色 tonal scrim 4%，允许区间 2–6%
tone.recessed	相邻内容面比主承载面低一个 tonal 档
light.direction	左上方单一光向
rim.width	约 0.5–0.75 DIP
ink.transition	120 ms
depth.primaryDelay	24 ms
depth.secondaryDelay	32 ms
depth.primaryTravel	6 DIP
depth.secondaryTravel	4 DIP
depth.mediaTravel	最大 8 DIP
depth.settle	跟随既有形变结束归零
page.transition	160–220 ms，短距离＋透明度
layout.reorder	180 ms，位置连续
reducedMotion	无视差、无位移，状态直接完成


这些是本项目参数，不声称是 Apple 或 Google 的规定数值。
D. 渲染管线、导航语义与小组件架构
D1. 一套文字语义，两个渲染前端
新增共享的纯数据排版目录：
TextRole
TypographyToken
InkRole
TypographyCatalog
FontResolutionPolicy
原生组件和 XAML 设置都读取该目录。
职责：
- Core：语义和纯数值。
- NativeBridge：将排版属性封送至原生。
- Native DWrite：字体匹配、布局、测量及绘制。
- XAML adapter：把同一 token 应用到现有 TextBlock／Button。
- 视图：选角色，不再临时填写任意字号和字重。
缓存键必须包括字体 profile、token revision、locale、角色、字重、tracking、字号、宽高和 DPI。不能沿用只包含字符串和颜色的旧缓存键。
D2. SurfaceOp 增量字段
保留现有属性及 Bold，新增：
public int FontWeight { get; init; }       // 0 表示兼容旧 Bold
public float TrackingEm { get; init; }    // 默认 0
public TextRole TextRole { get; init; }   // 默认 Legacy
public float LineHeightDip { get; init; } // 0 表示由字体布局推导
public float BaselineDip { get; init; }   // 0 表示由 metrics 推导
public TextFlags TextFlags { get; init; } // 含 TabularNumbers
public string? LocaleName { get; init; }
要求：
- 新业务通过角色解析生成这些字段。
- 旧插件绘制保持 Legacy 语义。
- 不将现有图标改成正文字体。
- 不删除 Bold 或改变原有 SurfaceOpKind 数值。
D3. Native ABI V2
当前 GDOp 为 x64 下固定 64 字节，必须保留旧入口和旧结构。
新增结构：
struct GDOpV2
{
    GDOp legacy;                 // 0..63，不改旧布局
    int32_t fontWeight;          // offset 64
    float trackingEm;            // offset 68
    uint32_t textRole;           // offset 72
    float lineHeightDip;         // offset 76
    float baselineDip;           // offset 80
    uint32_t textFlags;          // offset 84
    wchar_t const* localeName;   // offset 88
};

static_assert(sizeof(GDOpV2) == 96);
新增接口：
GDGetNativeAbiInfo
GDRenderSurfaceV2
GDMeasureTextV2
GDConfigureTypographyV2
接口约定：
- GDGetNativeAbiInfo 返回 ABI 版本、V1／V2 结构大小及能力位。
- GDRenderSurfaceV2 显式接收 operation stride，当前必须匹配 96 字节。
- GDMeasureTextV2 与绘制使用相同布局构造函数。
- GDConfigureTypographyV2 配置系统字体 profile；QA 可私有加载 Noto。
- 旧 GDRenderSurface 继续接受 64 字节 GDOp，保留旧语义。
- 新宿主检测到旧 DLL 时报告批次不兼容，不错误读取内存或静默丢弃排版字段。
C# 和 C++ 同时校验：
sizeof
Marshal.SizeOf
各字段 OffsetOf
enum 数值
指针宽度
调用约定
原生 ABI 版本与插件 SDK 版本分别管理，不因原生 ABI V2 就把 PluginApi.Version 改为 2。
D4. DWrite 实施步骤
按固定顺序：
1. 根据 role／字号／profile 选择 Small、Text、Display 或可用 Variable 能力。
2. 根据 locale 和脚本选择字体回退。
3. 使用数值字重创建 TextFormat。
4. 创建 TextLayout。
5. 应用逐 run 的字重、tracking 和数字特性。
6. 应用行高、基线、对齐及省略规则。
7. 输出测量结果或执行绘制。
实现要求：
- 使用 IDWriteTextLayout1::SetCharacterSpacing，单位由 em × fontSizeDip 转换。
- CJK run tracking 固定为 0。
- 不按 UTF-16 单个字符拆分绘制，不破坏代理对、组合字符、emoji 或双向文本。
- 使用 DWrite 分析及 fallback 能力；保留系统 fallback 作为最终回退。
- 根据 UI locale、内容脚本处理中文、日文、韩文，不能把所有 CJK 强制设为简中。
- 记录实际字体及模拟标志。
- 保留透明表面的灰阶抗锯齿。
- 不通过字形 outline 或额外偏移实现字重。
对应 API：CharacterSpacing、FontFallback。
D5. 测量与 tnum
移除“仅凭字符串长相判断时间”的新业务依赖，改为显式语义：
内容	数字规则
HH:mm、HH:mm:ss	tnum，tracking 0
播放位置和总时长	tnum，tracking 0
小组件数值、百分比、温度	tnum，tracking 0
日期中的日、年等数值段	对数字段应用
歌名、歌手和说明	不自动施加 tnum


不同字符数量的数值使用稳定字段宽度及对齐，例如 9% 与 100% 保持字段边缘稳定；不能指望 tnum 自动让它们整串等宽。
音乐胶囊宽度测量改用 GDMeasureTextV2，与实际绘制共享字体和布局参数。保留现有宽度上下限和右侧频谱定位公式。
缓存必须有上限，建议 TextFormat 64 项、TextLayout 128 项；使用有界淘汰，不长期保留歌曲历史。
D6. 设置 XAML 最小改造
允许修改：
FontFamily
FontSize／FontWeight
CharacterSpacing
Foreground
必要的文字行高和基线属性
共享文字样式的引用
不修改：
分类与顺序
导航位置及几何
Grid 行列结构
SettingsColumns 响应式规则
卡片及控件排列
主要 Margin／Padding
蓝色资源
浅色主题
tracking 转换：
XAML CharacterSpacing = round(TrackingEm × 1000)
CharacterSpacing 的单位为千分之一 em。Microsoft 文档
混排时不对整个 TextBlock 设置负 tracking；纯 Latin 标题才应用负值，CJK 保持 0。数字通过 XAML 的 tabular numeral 能力处理。
先在一个标题、一个设置行、一个说明文本上验证，再推广至五页。若新文字超出既有槽位，优先修正字体匹配和文本策略，不改设置结构救场。
D7. 导航：保留五槽，增加卫星
真实页面与视觉选择分开：
ActiveDetailsPageId = music | widgets
SelectedPreviewItemId = 原有五项之一
路由表：
操作	视觉选择	真实页面
点击音乐项	原有居中弹簧继续	切换到音乐
点击时钟／陪伴／专注／随记	保留既有视觉预览	不切换真实页面
点击右端小组件卫星	不增加第六槽	切换到小组件
关闭后重开	保留原有导航出场表现	恢复最后真实页面


语义层：
- 悬停显示名称。
- 未接入业务的四项明确标为“预览”。
- 脸下方显示真实页面指示：“音乐”或“小组件”。
- 卫星在小组件页显示可见选中状态。
- 辅助技术同时区分“视觉预览选择”和“当前真实页面”。
- 删除目前不再准确的“音乐正文保持显示”等固定描述。
卫星默认几何，基于展开后的 360 DIP 面板：
项目	值
原导航	继续 264×48，位置及五槽几何不变
卫星可见尺寸	28×28 DIP
与导航右端间距	8 DIP
卫星中心	导航右端向右 22 DIP，垂直居中
点击目标	以卫星中心计算 44×44 DIP，不得覆盖原五槽
图标	清楚的四格小组件图形
名称	小组件


实现为独立 visual／交互模块，读取现有导航边界，不修改五槽数量、环绕模数、spacing、ExpandedWidth 或原弹簧参数。
卫星在 Details 内容基本显现、导航展开到可用状态后出现；不穿过胶囊或小展开保护区。
眼睛：
- 指针或键盘语义焦点落在项目上时，朝该项目方向看。
- 指针离开时，朝当前真实选中入口看；音乐可回正，小组件轻微看右侧。
- 遵守已有跟随鼠标开关。
- 简化动画时使用静态眼位。
- 不新增第二双眼睛或独立陪伴身体。
本轮不实现第六槽。若卫星在实际尺寸中无法满足命中和边界要求，提交具体截图与几何报告，再讨论六槽；执行者不得自行切换方案。
D8. 页面记忆及布局策略
新增 DetailsPageController，作为真实页面唯一权威来源。
偏好升级至 v4：
public string LastDetailsPageId { get; init; } = "music";
规则：
- 只接受 music、widgets。
- v1／v2／v3 迁移默认音乐。
- 未知值只回退页面，不重置整个配置。
- 真实页面提交后更新内存状态并防抖保存。
- 关闭、隐藏和正常退出前刷入。
- 旧设置保存请求不得把该字段写回旧值。
- 主协调器提交设置时，以页面控制器的当前值覆盖旧快照中的页面字段。
页面记忆含义：
- 再次展开恢复页面。
- 重启恢复页面 ID。
- 不冻结关闭时的歌曲或时间；重开显示最新数据。
- 页内滚动位置在当前进程保留，重启从顶部开始。
- 关闭大展开不改变现有胶囊显示策略。
布局：
- 音乐页沿用当前连接／时间轴对应尺寸。
- 小组件页宽度默认 360 DIP。
- 小组件页高度由卡片内容决定，默认两张小卡片时约 304 DIP。
- 高度最多 512 DIP，再受工作区限制；超出部分滚动。
- 音乐断连不得把正在显示的小组件页缩成音乐空态。
- 音乐状态、频谱和时间轴只在对应音乐界面可见时刷新；媒体接入和播放不因切页停止。
D9. 小组件页首版规格
首版是大展开内部的组件板，不创建独立桌面窗口，也不支持拖出成为桌面组件。
提供：
- 添加；
- 移除；
- 排序；
- 小／中尺寸切换；
- 单实例配置；
- 多实例；
- 空状态；
- 不可用状态。
默认布局：
顶部：保留陪伴导航与卫星
导航下：真实页面指示
工具行：我的组件 / 添加 / 编辑
内容：时间小卡片 + 日期小卡片
默认双列参数：
项目	值
左右留白	24 DIP
列间距／行间距	12 DIP
小卡片	150×148 DIP
中卡片	312×148 DIP，跨两列
内容起点	约 y=132 DIP
底部留白	24 DIP


窄工作区不足以容纳双列时改单列，但不重写已保存的卡片顺序或尺寸选择。
默认两张同尺寸卡片并排，避免首屏一侧留出大块空位。
卡片使用共享背景材质和轻 tonal 承载面，不各自创建一套背景采集、模糊或液态玻璃管线。
D10. 时间与日期组件
时间组件
- 默认本地时区。
- 24 小时制默认。
- 可选 12 小时制。
- 秒默认关闭。
- 可选择 Windows 已有时区。
- 不下载时区数据库。
- tnum，数值边界稳定。
日期组件
- 本地日期。
- 显示日、年月和星期。
- 可配置是否显示星期及简／长格式。
- 本轮不加入日程、节假日联网、农历服务或事件提醒。
配置必须按实例保存，不复用或修改时间白板的 ClockShowTime/Date/Weekday。
刷新：
- 无秒时钟按分钟边界更新。
- 有秒时钟仅可见时按秒更新。
- 日期按对应时区午夜更新。
- 恢复、系统时间和时区变化时立即刷新。
- 隐藏时停止调度，重新显示直接取当前时间。
D11. 调整与配置交互
编辑模式：
- 显示拖动把手、尺寸和移除操作。
- 不持续摇晃卡片。
- 拖动超过 6 DIP 才捕获。
- 释放后按顺序重新排布。
- 中卡片跨两列，小卡片按顺序填充。
- 存储顺序及尺寸，不保存依赖当前窗口宽度的绝对像素坐标。
- 拖动到组件板外释放时，撤销本次拖动，不误收起整个 Details。
“完成”提交编辑；“取消”恢复进入编辑前状态。外部点击收起时，提交最后有效布局后收起，不弹阻塞对话框。
单实例配置使用轻量 WinUI 配置窗口／面板，复用本轮字体和墨色，不改设置 IA。
该配置窗口及其下拉弹出层注册为组件拥有的交互区域：
- 点击其中不收起父组件；
- 点击它们之外仍按现有规则收起；
- 父组件隐藏或关闭时清理子窗口；
- 不把所有同进程窗口都视为组件内部。
D12. 组件存储
保留主偏好的 64 KB 限制。卡片配置单独保存到同一预览 profile 下的 widgets.json。
最小结构：
WidgetBoardState
  Version = 1
  Instances[]

WidgetInstance
  InstanceId
  ProviderId
  Size = small | medium
  Order
  Settings
规则：
- 上限 32 个实例。
- 每实例配置上限 4 KB。
- 整个文件上限 256 KB。
- 原子写入。
- 文件不存在时创建默认时间、日期。
- 合法空列表表示用户删空，不重新塞回默认卡片。
- 无效文件保留备份，再恢复可用状态。
- 缺失插件的卡片保留配置，显示“组件暂不可用”，允许移除。
- 不因插件缺失重置其他卡片。
D13. 本地插件组件贡献
沿用现有可信、进程内插件模型，不改为联网市场。
通过新增贡献类型扩展，不给现有 IPluginContext 增加必实现方法：
PluginWidget : IPluginContribution
  Id
  Name
  Descriptor
  Provider

IWidgetProvider
  GetSnapshotAsync(request, cancellationToken)

WidgetRequest
  InstanceId
  Size
  UtcNow
  Settings

WidgetSnapshot
  Elements
  NextRefreshUtc

WidgetElement
  Kind
  Bounds
  Text
  TextRole
  NumericStyle
  SymbolKey
  CommandId
首版元素限定为文字、宿主图标、分隔线、进度值。字体及墨色由宿主角色控制，不允许插件直接注入网页或自建窗口作为卡片。
约束：
- 单快照最多 64 个元素。
- 元素必须位于卡片范围内。
- 字符串及坐标有界，拒绝 NaN／Infinity。
- 命令只能引用已注册命令。
- 每实例最多一个在途请求。
- 仅可见卡片及一行预取范围调度。
- 普通 provider 在后台执行，不阻塞 UI 线程。
- 2 秒超时后停止该实例刷新，旧调用完成前不启动新调用。
- 旧代次结果不得覆盖新尺寸、新配置或重新打开后的视图。
- 错误保留最后成功结果或显示不可用，不破坏时间／日期卡片。
- 不声称进程内接口能强行终止不合作代码。
PluginApi.Version 保持 1，保留旧构造函数和旧贡献类型。旧插件在新宿主中必须继续加载；新组件贡献要求本轮宿主。
D14. 插件稳定性门槛与降级
只有以下项目均通过，才在交付包开放第三方卡片贡献：
1. 正常贡献加载、显示、配置、改尺寸和移除。
2. provider 抛出普通异常时，宿主与其他卡片继续工作。
3. 超时、取消和晚到结果处理正确。
4. 越界、非法角色、过量元素被拒绝。
5. 连续启停／隐藏／恢复无残留订阅和请求。
6. 插件移除后实例配置仍保留。
7. 当前构建的新增稳定性测试及实机证据完整。
任何一项未完成或失败：
- 交付时间／日期内置卡片；
- 暂不开放第三方卡片目录；
- 保留接口代码与测试记录；
- 不影响原有插件系统；
- 报告标明“内置组件模式”，不能宣称完整插件组件能力通过。
E. 深度机制
E1. 层次结构
真实背景／既有磨砂或液态材质
→ 内容承载面的轻 tonal scrim
→ 清晰文字和图形
→ 操作状态、焦点和陪伴语义
- 继续使用现有 WGC worker→GPU→Composition 管线。
- 本轮不重写采集架构，不给每张卡片启动采集。
- 控件接触阴影与 rim 保持克制。
- P1 白板、频谱及设置蓝色不被新效果覆盖。
E2. Tonal scrim
默认 4%，允许 2–6%。
- 按内容承载面应用，不逐字加底板。
- 不形成文字标签形状的白色补丁。
- P1 时间白板内部不叠加新 scrim。
- 频谱保护区域不叠加新材质。
- 设置在既有浅色材料内做轻 tonal 区分，不改变主题和布局。
实际对比不足时提升墨色；无法取得可信背景信息时，不假称四点外围取样能够保证整片正文对比。
E3. 单一光向 rim
- 统一左上光向。
- 同一控件不叠多方向高光。
- rim 只用于材质边界。
- 不改 P1 频谱的 SoftBar 材质分支。
- 不增加大面积模糊阴影或持续移动光带。
- 高对比模式沿用系统颜色和清楚边界。
E4. 形变视差
应用于非保护区的曲目信息、封面承载内容及新卡片内容层。
默认：
层	延迟	额外位移
主文字	24 ms	6 DIP
次文字	32 ms	4 DIP
封面／主要媒体	20 ms	最大 8 DIP
主要按钮命中层	0	0
时间白板、频谱	0	0


规则：
- 由现有形变时间轴采样延迟进度，避免新增永久计时器。
- 不用真实 sleep 推迟渲染。
- 支持中断，从当前状态重定向。
- 结束时位移严格归零。
- 捕获或拖动期间停止非必要视差。
- 页面滚动不跟随鼠标产生视差。
- 角色安全区域按实际呈现内容边界计算。
- 不通过文字缩放制造深度。
E5. Ink crossfade
- 同一明暗家族中的角色墨色变化使用约 120 ms 过渡。
- 不每帧重建 DWrite layout。
- 优先使用缓存文字遮罩和颜色 brush；彩色 glyph 保留独立路径。
- 明／暗墨色切换不能经过长时间低对比灰色。
- 无法保证中间帧对比时，立即切到安全墨色，不强制淡化。
- 不让文字在过渡中出现双影或透明度凹陷。
E6. Reduced motion 静态等价
开启系统或应用简化动画时：
- 视差、拉远、额外位移关闭；
- 墨色直接切到最终状态；
- 页面直接切换；
- 拖动后的布局直接落位；
- hover 名称、选中指示、页面记忆全部保留；
- 陪伴遵守既有简化动画规则；
- 信息和操作不能依赖动画才能理解。
F. 分阶段执行、产物与验收
F1. 实施顺序
阶段	内容	必须产物	阶段门槛
P0 基线冻结	源码、截图、P1 参数、布局、资源和运行批次	基线清单、保护区定义、现状诊断	能追溯“改前是什么”
P1 字体取样	现状／A 优化／B Noto 的 200% 对照	样张、字体记录、度量报告、token 表	A 可接受；否则提交 B 评估材料
P2 排版管线	ABI V2、DWrite、统一测量、XAML adapter	ABI 报告、文字矩阵、静态界面截图	布局、混排、数字和对比通过
P3 材质与动效	tonal、rim、视差、ink 过渡	静态对照、连拍、简化动画证据	P1 不变，动画可中断并归零
P4 真实页面	卫星、导航语义、页面路由和记忆	两页操作证据、重启读回	五槽原样，页面恢复正确
P5 内置组件板	时间、日期、排序、尺寸、配置、存储	组件板截图、配置样本、恢复测试	全离线，正常编辑与恢复
P6 插件贡献	新贡献类型与稳定性门槛	测试插件、生命周期与异常证据	全通过才开放，否则内置模式
P7 集成交付	回归、资源、文档、预览包	验收矩阵、哈希、限制说明	无未报告失败、包与源码对应


不得在 P1 样张和 P2 静态排版尚未完成时，用更多动画掩盖问题。
F2. 可使用的子智能体分工
执行阶段允许：
角色	负责
主 agent	共享契约、P1 保护、主协调器、WidgetScene、路由与最终集成
原生排版 agent	ABI V2、DWrite、测量、缓存、字体 probe
字体／XAML agent	token、200% 样张、设置最小排版改造
小组件 agent	组件板、实例存储、provider 调度和测试


约束：
- 先冻结共享类型再并行。
- 不允许多个 agent 同时重写 WidgetScene。
- 导航核心五槽模型不交给 agent“顺手重构”。
- 构建和原生 GUI 操作由主 agent 统一串行调度。
- 不自动安装设计技能或引入新的 UI 框架。
F3. P1 零改动验收
基线阶段记录：
时间白板：
  x/y/width/height/radius
  日期开启时的既有高度规则
  填充策略

频谱：
  count=8
  bar width/spacing/rest height
  各形态 offset
  target 计算和音频来源
  SoftBar 材质

设置：
  分类、顺序
  Grid/Columns/导航/布局参数
  浅色主题
  有效 accent=#1689F9

陪伴导航：
  count=5
  ExpandedWidth=264
  Height=48
  Spacing=38
  原环绕规则与弹簧参数
验收同时使用：
1. 受保护参数／方法片段 diff；
2. 原生几何读回；
3. 同条件截图保护区比较。
文字字形区域单独标记，不用授权字体变化制造“保护项失败”，也不能因此忽略白板或频谱的几何变化。
当前项目存在整目录未跟踪的 Git 状态，执行者必须先核实。不能仅凭空 git diff 宣称零改动；必要时使用独立源码快照、片段摘要与文件清单。
F4. 字体与数字验收
编号	场景	标准
T01	100%、150%、200% 渲染	无裁切、缺字、彩边和伪粗体
T02	200% 中拉文比较	附实际 face；笔画重量没有明显突变
T03	中文与 Latin 基线	同行没有异常跳升／下沉
T04	CJK 字距	tracking 为 0
T05	HH:mm 全日组合	同长度字段 advance 波动目标不超过 0.05 DIP
T06	HH:mm:ss	数字变化不导致字段横向抖动
T07	9%、99%、100%	字段边缘稳定，文字不压缩
T08	长歌名、多语言歌名	省略正确，可访问名称完整
T09	Native 测量与绘制	相同参数、相同字族、相同宽度结果
T10	XAML 设置	原布局可用，无新横向溢出或控件遮挡


中拉文重量比较使用代表性直笔画和视觉审查，不比较整字黑色像素总量。若 A 在多个常用字号下持续出现显著差异，触发 B 评估；不得为通过数字指标修改字体轮廓。
F5. 对比与材质验收
对浅、深、灰、彩色复杂背景分别记录：
区域
文字角色
实际墨色
最终背景取样
对比度
是否提升墨色档位
是否使用回退
通过标准：
- 必读小字达到 4.5:1。
- 大数值达到 3:1。
- 必要图标／边界达到 3:1。
- Ink 4 不承载唯一必要信息。
- 无文字描边、发光、内阴影。
- 无新增逐字白色底板。
- 设置仍为浅色、原布局、原蓝色。
材质诊断图、原始背景图和最终 UI 截图分别命名，不能混用。
F6. 动效验收
- 取开始、约 25%、50%、75%、结束及结束后一帧。
- 对照外壳、主文字、次文字、封面的相对位移。
- 延迟在 20–40 ms 目标范围内。
- 位移在指定 4–8 DIP 范围内。
- P1 区不参与额外视差。
- 快速反复开关和切页后不残留偏移、双影或旧回调。
- 简化动画下仍有清楚的名称、选中状态和完整信息。
- 结束后停止动画更新，不长期保留 60 Hz 空循环。
连拍是受控界面证据，不默认录制用户桌面长视频。
F7. 导航与页面记忆验收
1. 原五槽宽度、间距、环绕和弹簧与基线一致。
2. 卫星没有挤压五槽。
3. 卫星有正确名称、命中区域和选中状态。
4. hover 名称不挡住封面或脸。
5. 四个预览项不误触发真实路由。
6. 音乐项可以返回音乐页。
7. 小组件→收起→展开，仍为小组件。
8. 音乐→收起→展开，仍为音乐。
9. 隐藏／显示、设置保存、切歌、断连、恢复不重置页面。
10. 重启后恢复最后页面。
11. 坏页面 ID 只回退页面，不重置其他配置。
12. 快速切页后的旧异步结果不覆盖当前页。
F8. 小组件验收
- 默认时间／日期正确且离线。
- 默认两卡平衡排布。
- 添加、删空、多实例、排序、改尺寸、取消编辑均正常。
- 外部收起提交有效布局，不吞外部点击。
- 拖动外部释放不误收起。
- 时间制式、秒、时区、日期格式互不影响其他实例。
- 不改时间白板偏好。
- 午夜、时区变化、系统时间变化及恢复后刷新正确。
- 隐藏后停更，重开立即更新。
- 配置窗口及下拉层属于组件内部交互范围。
- 缺失／故障插件不会破坏其他卡片。
- 未通过插件门槛时，内置模式明确、可用、无假入口。
F9. 性能不回退
采用相同机器、DPI、背景、媒体和插件开关，分别测当前基线与候选版本。基础音乐场景与新增组件板场景分开。
记录：
- 宿主与 worker CPU；
- 私有内存；
- 句柄；
- GPU 记账；
- TextFormat／TextLayout／文字表面数量；
- 实际呈现数据；
- 静止后的调度活动。
门槛：
- 静止排版不触发持续重排或重绘。
- 默认时间／日期不增加每帧循环。
- 无持续 CPU 像素读回。
- 同条件核心场景的 CPU／GPU 在测量噪声范围内；CPU 增量超过 0.10 个归一化百分点须分析并处理。
- 新视图全部加载后的额外私有内存目标不超过 15 MiB；B 字体评估单独统计。
- 动画结束后返回静止基线。
- 有界缓存进入平台，无持续线性增长。
- 帧时间回归超过 10% 需要处理，不能以视觉改善为由直接接受。
- 实际 Present 未取得时标记未测，不能用计时器频率代替。
现有液态玻璃短窗口内存趋势仍是已知待观察项，不能在本轮报告中消失，也不能将不同短记录拼成连续驻留通过。
F10. 构建、验证与交付
保持当前 .NET、Windows SDK、Windows App SDK、自包含和原生架构，不为字体或参考代码升级框架。
继续使用锁文件和串行构建。保留 PRI／XBF 发布校验。
GUI 验证使用用户指定的 [cua-driver](C:/Users/Sheng/.cua-driver/skills/cua-driver/SKILL.md)：
- 新鲜快照后操作；
- 精确目标窗口；
- 操作后读回；
- 外部点击、悬停和拖动用实际指针验证；
- 不自动改变系统缩放、壁纸、启动项或正式快捷键。
新预览沿用已实现的实例与配置隔离，为 preview.2 使用独立 profile。不得让 v4 配置被旧预览误读，也不直接改写正式用户配置。
交付目录：
D:\Believe_in_yourself\about AI\work about agents\codeX\codeX work\GlassDesk\artifacts\qa\typography-widgets-preview2

D:\Believe_in_yourself\about AI\work about agents\codeX\codeX work\GlassDesk\artifacts\ui-preview\0.3.0-preview.2

D:\Believe_in_yourself\about AI\work about agents\codeX\codeX work\GlassDesk\artifacts\ui-preview-data\0.3.0-preview.2

D:\Believe_in_yourself\about AI\work about agents\codeX\codeX work\GlassDesk\artifacts\release\0.3.0-preview.2
必须包含：
- 本计划及最终 token 表；
- 现状诊断与受保护项清单；
- 200% A／B 样张及字体解析记录；
- ABI 布局和兼容测试结果；
- 对比度矩阵；
- 静态界面、视差连拍、简化动画证据；
- 页面记忆与配置迁移结果；
- 插件贡献门槛结果及实际交付模式；
- 性能对比及未测边界；
- 可运行预览、源码对应信息、SHA-256；
- 第三方许可与已知限制。
保护 0.2.0 和 0.3.0-preview.1 的既有发布目录，构建前后核对哈希。本轮不自动覆盖安装器或正式安装。
G. 风险、已拍板项与条件性决策
项目	状态	处理
字体 A／B	已选 A	先出样张；A 不足时评估 Noto B，不自动切换
中拉文实际字重	需实测	记录实际 face；不用请求值冒充真实字重
导航语义是否纳入	已纳入	只补语义和卫星，五槽视觉核心不重做
第六槽	本轮不做	只有用户改变决策后才重算六槽几何
强调色是否跟随陪伴色	已否定	设置保持 #1689F9
小组件真实页面	已确认	与音乐构成两个真实页面
页面记忆	已确认	持久化真实页面 ID，不持久化旧歌曲画面
第三方卡片稳定性	条件性开放	未验收则交付内置时间／日期模式
天气	明确延期	下一轮单独处理联网、城市、状态和隐私
2–6% scrim 的效果	有限	不能代替对比度验证和必要回退
ABI 混用	高影响技术风险	V1 保留、V2 协商、同批次 DLL 校验
P1 保护与文字更新	已限定边界	布局／行为冻结，授权排版变化单独审查
进程内插件	沿用现状	不宣称 SDK 是网络或资源安全沙箱
性能证据	必须新增	历史测试不替代新版本结果


可参考的开源实现限定用途：
- Material Web typography tokens：参考角色、字号、字重、tracking 的 token 组织方式，按 Apache-2.0 要求处理实际复用内容；不引入网页组件运行时。
- WinUI Gallery：参考原生文字、控件和可访问行为；不因此重做设置布局。
- Apple Widgets：参考信息聚焦、尺寸变化和清晰的内容层级；本轮组件仍位于 GlassDesk 大展开内。
本计划没有需要再次询问的常规实施选择。只有 A 字体经样张验证仍不合适、或 用户希望改成第六槽 等范围变化，才提交具体证据重新决策。
最终完成定义
本轮完成必须同时满足：
- 字体、墨色和排版角色贯通胶囊、小展开、大展开与设置。
- 内部远近感来自层级、材料和短暂相对运动，文字没有凹凸装饰。
- P1 保护项零结构／行为改动。
- 原五槽视觉核心保留，卫星和语义层可用。
- 音乐／小组件真实切页，关闭和重启后恢复正确。
- 时间／日期卡片可调整、离线且配置独立。
- 插件能力按实际稳定性门槛交付，不虚报。
- 天气未被偷偷接入。
- 新 ABI、旧插件及现有媒体行为通过适当回归。
- 性能、证据、交付包和源码批次可追溯。
</proposed_plan>
  
3:16