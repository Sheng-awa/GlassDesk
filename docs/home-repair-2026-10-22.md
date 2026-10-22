# 首页视觉修复试用版 · 2026-10-22

状态：代码与基础检查完成，视觉/交互等待用户试用。不是已经通过人工视觉验收的正式发布。

## 试用入口

目录：[home-repair-2026-10-22](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/artifacts/ui-preview/home-repair-2026-10-22>)。

运行目录里的“启动试用版.lnk”快捷方式或GlassDesk.exe。先从托盘退出旧版，便于只观察一个胶囊。保留完整运行目录，不只拷贝exe。

这是独立预览实例，设置保存在运行目录的preview-data。首次试用配置以旧preview.2外观配置为底，在这份新副本里开启液态玻璃、将上次页面设为首页、跳过初次设置引导；没有修改原预览或正式版配置。之后页面按你关闭前的选择记忆。若第一次停在音乐/小组件页，点左上箭头返回首页。

没有自动启动或安装本版，没有退出用户正在运行的实例。

## 这次具体改了什么

| 审查项 | 本轮改动 | 验证状态 |
|---|---|---|
| F01 浅色可见度 | expanded用深色文字；导航/Dock有稳定的浅色材质底与轻边界，眼睛、图标与辅助文字不再固定浅白 | 编译通过，待用户看白底 |
| 液态玻璃 | native保留实时磨砂底层，在其上以34%不透明度叠加现有液态材质；复用现有采集/着色结果，不另建采集链 | native编译通过，未测GPU增量 |
| F02 导航 | 非对称弧形路径、圆角父容器clip、从左下滑入；260ms主动作及30ms错峰；停在可预测的独立44DIP功能目标 | 代码/几何检查通过，待试手感 |
| F03/F04 文件夹 | 主体按参考缩为34×24附近；统一前盖/tab轮廓，纸层闭合隐藏；底边pivot、独立rotateX与120DIP透视；软椭圆接触影 | 编译通过，待观察边缘与开合 |
| F05 动画路径 | HomeVisual持久visual分层，弧片/翻盖/眼神/气泡/应用hover走合成属性动画；静态文字不再随首页动画每帧重绘 | 源码路径确认；未录屏或做帧时分析 |
| F06/F08 气泡 | 完整淡入、缩放、位移和退场；保留退出快照；同文件夹再点关闭、换项替换，外点先关闭且不启动背后入口 | 编译通过，待用户点击检查 |
| F07 命中 | 导航三个功能目标独立、不嵌套、不重叠；功能图标位置稳定，装饰弧片在其下运动 | Core检查通过 |
| F09 短窗口 | 文件夹Dock固定底部，上方网格/导航可滚动；短窗口先收紧行距与行高；窄屏按实际宽度排布 | 320/360宽及300/360/400/512高的基本几何检查通过 |
| F10 页面记忆 | 删除打开大展开强制home的覆盖；音乐/小组件保持原页；两个子页加独立返回首页箭头，顶部陪伴把手仍用于收起 | 编译/Core基础检查通过，待用户往返 |
| F11/F12 系统偏好/眼睛 | 首页支持减少动态和系统高对比；眼神同区移动持续更新，遵循跟随设置；让开后本次展开不再自动藏回 | 编译通过，待用户体验 |
| F13 文件读取 | 目录元信息后台读取、取消、超时、旧结果隔离；loading/empty/error单独显示；默认位置按系统桌面/文档/下载解析，名字不再冒充crucial | 编译通过，未扫描用户目录做批量测试 |
| F14 资产/长名 | 从本机已安装应用取图标，缺失使用可读fallback；文件名使用两行真实文本，不按UTF-16前11字截断；通用文档不再假装全是PDF | 编译通过，待用户实际文件检查 |
| 错误提示 | 首页错误提示在自己的前景层显示，避免被导航或Dock遮住 | 编译通过 |

功能目标与装饰分开是有意调整：导航图标/44DIP命中保持稳定，弧片在其下滑入，不追着移动中的图标才能点。

## 已做的简单检查

- 原生Release编译成功，没有运行原生回归/采集循环。
- App Release编译：0警告、0错误。
- 自包含预览publish成功，包含GlassDesk.pri、App.xbf、native DLL和采集子进程。
- Core普通检查：**303通过**（使用--no-stress，跳过压力循环；包含6条本轮几何检查）。
- Git差异检查按仓库现有CRLF格式完成；没有将源文件整批改成另一种换行。
- 没有录屏、复杂UI自动化、长期驻留或GPU/FPS测量，不据此宣称性能已验收。

## 你先试这四件事即可

1. 白色窗口/白壁纸上：标签、眼睛、导航和Dock是否清楚，玻璃是否保留磨砂而不过分透字。
2. 鼠标反复进出导航与文件夹：弧面是否自然，翻盖有没有白角、断边或突然跳回。
3. 点文件夹→再点同一个→点另一个→点气泡外：开关和切换是否符合预期。
4. 去音乐/小组件→收起→再开：应留在关闭前那一页；左上箭头能回首页。

反馈可以直接给截图和你觉得不顺的动作；先不用录视频或跑额外工具。

## 本轮边界

- 184×32时间白板、8条静息频谱、设置IA/布局/浅色/蓝色accent均未重设计。
- 没有更换技术栈、字体包，未引入天气/联网插件市场。
- 默认文件夹本轮仍是系统桌面/文档/下载，未增加自定义路径设置页面。
- 原生普通键盘导航仍沿用既有能力；本版不通过激活浮窗抢走当前工作应用焦点来强行补键盘模式。
- 新的视觉和动画需要你实际试用后确认，不把编译成功写成人工验收通过。

## 主要代码入口

- [HomeVisual.cs](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeVisual.cs>)：独立合成层与动画。
- [WidgetScene.Home.cs](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/WidgetScene.Home.cs>)：状态与统一命中。
- [HomePresentationLayout.cs](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Core/HomePresentationLayout.cs>)：实际viewport几何。
- [HomeDirectoryService.cs](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.Platform/HomeDirectoryService.cs>)：本地目录元信息。
- [HomeIconLoader.cs](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/src/GlassDesk.App/HomeIconLoader.cs>)：本机应用图标。
- [GlassDeskNative.cpp](<D:/Believe_in_yourself/about AI/work about agents/共同工作区/GlassDesk/native/GlassDesk.Native/GlassDeskNative.cpp>)：磨砂与液态叠层。

