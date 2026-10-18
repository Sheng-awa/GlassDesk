# 0.2.0 进程音频与真实频谱

## 实现与宿主接入

`ProcessAudioVisualizationSource` 实现 Core 中冻结的 `IAudioVisualizationSource`。宿主用 `new ProcessAudioVisualizationSource(uiDispatcher)` 创建实例，订阅 `FrameChanged`，在媒体、窗口可见性、形态或波浪条开关变化时调用 `SetTarget(media, active)`。`active` 表示当前确实存在可见波浪条；源码还独立要求媒体连接、非空会话标识及 `Playing` 状态。暂停、隐藏、切为不含波浪条的内容或关闭开关会即时发布零值并唤醒工作线程停止采集。程序退出时释放实例。

没有 dispatcher 时事件在分析线程运行；生产宿主必须传 UI dispatcher。异步排队的旧事件会检查会话代次和当前帧，不能在暂停或会话替换后恢复旧频谱。元数据更新不会重启同一会话的采集；恢复、会话替换和 PID／创建时间变化会重新绑定。每 500 ms 重新验证来源；捕获失败后最多每秒重试一次。不可用或静音的重复零帧不触发持续重绘。

| 原生 ABI | 行为 |
|---|---|
| `GD_AudioCreate(DWORD pid, uint64_t creationFileTime, void** capture)` | 核对 PID 和创建时间，持有进程句柄，激活其进程树 loopback；返回 HRESULT |
| `GD_AudioRead(void*, float* stereo, UINT32 capacityFrames, UINT32* frames, UINT32* rate, int64_t* qpc)` | 读出可复用调用者缓冲区；单位为双声道帧，固定 48 kHz IEEE float，QPC 单调时间戳；没有新数据返回 `S_FALSE`／零帧 |
| `GD_AudioDestroy(void*)` | 通知采集线程停止，等待线程退出，释放音频与进程句柄 |

`ProcessAudio.cpp` 独立编译进 `GlassDesk.Native.dll`，链接 `mmdevapi.lib` 与已有的 `ole32.lib`。ABI 调用只能由同一采集工作线程顺序使用；不能并发销毁正在读取的原生句柄。原生线程在 MTA 中工作，使用独立可取消事件及有界激活等待。COM 激活回调自持生命周期，超时后的晚回调不引用已释放的采集对象。

## 来源与数据边界

Windows GSMTC 的 `SourceAppUserModelId` 只提供 App ID，没有 PID。现有网易云适配器固定识别 `cloudmusic.exe`，解析器据此只接受文件名为 `cloudmusic.exe`、版本信息 `CompanyName=NetEase`、`OriginalFilename=cloudmusic.exe` 的进程。只有一个可验证的网易云进程树时才绑定；同一树的子进程不会作为独立实例。多个根进程、无法验证的文件、失效的 PID、异常树或权限失败都显示不可用。

解析器用创建时间防止已退出父进程的 PID 重用将两个实例误归为一树；原生层再次核对根进程的创建时间并持有该进程句柄，检测其退出。选定模式始终是 `PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE`，新子进程由 Windows 进程树捕获规则覆盖。没有全系统／默认端点采集回退，没有麦克风接口。

原生环形缓冲区复用 8,192 双声道帧。托管分析复用 2,048 帧输入及 FFT 工作数组，以 25 Hz 计算 RMS 和两声道各自的 Hann 窗 radix-2 FFT，再合并能量，避免反相立体声相互抵消。八段边界为 20／60／150／400／1,000／2,500／6,000／12,000／24,000 Hz；显示值按 -60 至 0 dBFS 归一化到 0–1。交给 UI 的每帧八个数值有独立数组，已发布数据不被后续分析改写。没有新包、真实静音、采集失效或停止状态都归零，不生成随机数或替代波动。

原始音频只在当前进程内存中短暂存在，退出采集会清空托管缓冲区；没有音频文件保存、音频上传或歌曲时长推断。

## 2026-10-14 验证证据

独立测试工程为 `tests/GlassDesk.AudioChecks`，链接音频与契约源码，避免和并行编辑中的主工程构建输出竞争。原生音频 DLL 在 `artifacts/audio-checks/native` 单独构建，MSVC `/W4 /WX` 编译通过。托管工程零警告、零错误；首次还原因沙箱无法访问 NuGet 漏洞索引失败，使用 `-p:NuGetAudit=false` 运行本次没有第三方包依赖的隔离检查，未修改生产项目审计策略。

42 项隔离逻辑检查通过，覆盖七个已知频率与 RMS、反相立体声、静音／缺包／非有限值、多实例、文件身份无法验证、父及中间祖先 PID 重用、暂停／隐藏、元数据更新、恢复、会话替换、PID 变化、不可用归零、失败、订阅者异常以及回调中释放。

额外实际 WASAPI 信号测试合计 47 项通过。无窗口测试进程仅在内存生成 PCM，独立兄弟进程输出 4.3 kHz；捕获兄弟进程时确实检测到该频段，最大 RMS 为 0.23046683，而目标进程的捕获同时保持精确零。目标进程随后输出 1.5 kHz，连续 29 次分析落在预期频段，最大 RMS 为 0.21012296，首次检测时间为 108.4908 ms。这证明指定 PID 的真实采集、其他进程排除、FFT 及一次小于 150 ms 的音频分析响应。此测量没有包括宿主 UI 呈现，不能替代最终屏幕响应测量。完整输出保存在 `artifacts/audio-checks/native-signal-result.txt`。

另一次真实网易云来源探测唯一解析到 PID 27308，路径 `E:\nothing\网易云音乐\CloudMusic\cloudmusic.exe`；原生激活成功，25 次读取返回有限数值且最大 RMS 为零。该次没有由本子任务操作播放器，不能据此宣称网易云正在播放时的波浪条已完成实机验收。

最终 GUI 仍需由单一主执行者验证网易云和其他播放器同时播放、网易云暂停／静音、实际重启和 PID 变化、窗口隐藏与再次显示、波浪条开关及 UI 呈现延迟。真实客户端多实例因没有公开的会话到 PID 接口，按设计显示不可用；本版不猜测会话所属实例。

## 官方参考

- [Microsoft 进程 loopback 示例说明](https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/)
- [Windows-classic-samples / ApplicationLoopback 原生示例](https://github.com/microsoft/Windows-classic-samples/tree/main/Samples/ApplicationLoopback)
- [ActivateAudioInterfaceAsync 与激活回调生命周期](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-activateaudiointerfaceasync)
- [GSMTC SourceAppUserModelId 的实际契约](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssession.sourceappusermodelid?view=winrt-26100)

参考采用 API 路线与生命周期要求，本实现使用自有工作线程、内存环形缓冲区及频谱计算；没有引入示例 WAV 写入或 Media Foundation 工作队列。
