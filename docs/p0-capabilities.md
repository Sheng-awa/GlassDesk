# P0 实机能力记录

环境：Windows 11 25H2 26200.9550；网易云 3.1.41.205564；主屏 2560×1600 / 150%。

| 能力 | 结果 | 证据 |
|---|---|---|
| 精确网易云来源 | 已验证：`cloudmusic.exe` | `artifacts/p0/sources-after-smtc.json` |
| 歌曲名、歌手、封面 | 已验证，记录存在性与内容指纹，不记录歌曲历史 | `media-capabilities.json` |
| 播放、暂停、上一首、下一首 | 请求被接受，并由实际状态 / 内容版本读回确认 | `command-*.json` |
| 进度 / 跳转 | 本机来源未提供：所有时间轴值为零，CanSeek=false | `media-capabilities.json` |
| HostBackdrop + GPU GaussianBlur | 桌面截图验证棋盘格被实际模糊采样，20 DIP | `desktop-pointer-before.png` |
| 透明圆角点击到其他进程 | 实际输入：接收窗口计数 0→1 | cua RPC 15/16；`receiver.log` |
| 主体点击不激活 | 以接收进程为前台进行真实点击，宿主 down/up 的前台 HWND 都是接收窗口 4392040；接收窗口计数不变 | cua RPC 33/34；`glass-host.log` |
| WinUI 设置关闭后驻留 | 实际点关闭按钮后，仅原生宿主可见；同一进程保留 | cua RPC 30/31；`lifecycle-managed.log` |
| 再次打开设置 | 关闭后点击宿主，设置 opens 1→2 | cua RPC 39/40 |
| 拖动 | 已验证：修正 down 事件坐标后真实输入完整移动 200×140 px | `desktop-drag-corrected.png`；cua guarded loop |
| 释放资源 | WinUI + native 正常退出；宿主 HWND 消失，日志 clean-shutdown，进程结束 | `lifecycle-native.log` / `lifecycle-managed.log` |
| 运行中 DPI 恢复 | 缺陷已修复：保存 DIP 尺寸、使用新 DPI 消息、显示设置变化后读新建隐藏 PMv2 探针并重建尺寸。应用内 96/120/144/192 往返及停留旧 120→真实 144 回归通过；新宿主实测 540×198 / DPI=144 | `native-dpi-fix/dpi-regression.json`；`dpi-fixed-window.png`；只读窗口诊断 |
| 100/125/200% 及跨屏 | 全局缩放测试已停止；后续不自行改变系统设置。独立应用内 DPI 回归与真实跨屏验收分别报告 | — |

SMTC 原先关闭，用户明确授权后开启。它会向 Windows 和读取系统媒体会话的软件提供播放元数据。

媒体命令必须在创建 WinRT 会话对象的线程执行。第一次异步控制探针出现 RPC_E_WRONG_THREAD (0x8001010E)，改为在所属线程等待 WinRT 操作后重新测试成功；生产适配器应使用 UI DispatcherQueue 调度，而非线程池续体调用 COM 对象。

窗口局部 PrintWindow 截图无法证明 HostBackdrop 采样，应使用真实桌面截图。背景 PostMessage 也不能证明跨进程命中与焦点保持。驱动的前台交付会主动激活目标，所以焦点验收使用接收窗口为交付目标、真实指针落在覆盖它的 topmost / noactivate 宿主上，核对宿主事件内实际前台 HWND。

P0 基础通过后已完成 P1/P2 生产集成；此处保留独立探针的证据范围。最终界面、安装与驻留结果见 [测试报告](test-report.md)。

Windows 显示缩放已恢复原来的 150%（系统与显示器 DPI=144）。测试清单不构成改变全局系统设置的授权；后续需用户明确同意。SMTC 仍保持用户已明确批准的设置。
