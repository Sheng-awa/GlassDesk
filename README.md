# GlassDesk 0.2.0

Windows 11 悬浮玻璃音乐组件，接入网易云真实系统媒体会话，提供六种时钟／音乐形态、真实进程音频频谱、原创陪伴球、独立液态玻璃与本地功能插件。

0.2.0 Release 已构建，Core 47 项、Platform 23 项、插件 33 项、音频 42 项检查与原生回归通过。最终安装版完成 100 次液态玻璃 GUI 启停、1,000 次应用三态切换和超过两小时连续驻留；本机安装、升级、卸载保留配置和重新安装已验证。实测资源与未验证环境见 [测试报告](docs/test-report.md) 和 [能力矩阵](docs/capability-matrix.md)。

暂停五秒只改变胶囊内容，展开页保留真实歌曲与控制。音频仅采集可靠识别的网易云进程树，不回退系统全局声音；不可用或静音时归零。陪伴球默认开启、液态玻璃默认关闭。液态玻璃使用宿主拥有的 WGC 采集子进程和原创 D3D11/HLSL 后处理，呈现真实背景模糊与弧面边缘折射；采集失败时显示原因、回退磨砂并重试。进程内插件只适用于可信代码。

当前交付状态为候选版；其他 DPI、混合 DPI 和无开发运行时的干净 Windows 验证仍需专用环境。本机网易云未提供有效时间轴，界面明确显示不可用并禁用跳转。旧版驻留数字保存在历史报告中，不能代表 0.2.0。

## 安装与交付

统一交付目录为 `artifacts/release/0.2.0`，包含 `GlassDesk-0.2.0-win-x64-setup.exe`、`GlassDesk-0.2.0-source.zip`、两包共同的 `SHA256SUMS.txt`、验收报告和使用说明。安装器按当前用户安装并携带运行依赖，开机启动默认关闭，快捷键默认未绑定。发布二进制批次在 `verification/release-binary-batch-0.2.0.json`；交付包 SHA-256 在产物目录。

- [首次使用与操作说明](docs/user-guide.md)
- [可重建的构建说明](docs/build.md)
- [插件开发与本地打包](docs/plugin-development.md)
- [音频实现与证据](docs/audio-0.2.0.md)、[液态材质实现与证据](docs/native-material-0.2.0.md)
- [架构](docs/architecture.md) 与 [状态规格](docs/state-spec.md)
- [第三方许可](THIRD-PARTY-NOTICES.md) 与 `licenses/manifest.json`

## 工具链

- .NET SDK 10.0.401（global.json 固定）
- Windows SDK 10.0.26100.0，最低系统 Build 22621
- Windows App SDK 2.5.1
- MSVC x64 / C++/WinRT

## 媒体验证

```powershell
dotnet run --project tools/GlassDesk.MediaProbe
# 根据返回值填入精确来源，不自动使用系统当前播放器。
dotnet run --project tools/GlassDesk.MediaProbe -- --source cloudmusic.exe --out artifacts/p0/media.json
```

`--command play|pause|toggle|previous|next|seek` 只向精确来源发送命令。
seek 必须同时指定 `--ticks`。命令的返回值和后续状态均验证。
报告只记录内容是否存在、指纹和来源能力，不保存歌曲历史或账号数据。

