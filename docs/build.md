# GlassDesk 0.2.0 构建说明

目标为 Windows 11 x64，最低声明 Build 22621（22H2）。本次构建机为 Windows 11 25H2 / 26200.9550；最低版本声明和自包含发布配置不替代干净系统实测。其他电脑、干净 Windows 和混合 DPI 兼容性测试按用户安排留待下载试用，状态见 [能力矩阵](capability-matrix.md)。

## 固定工具链

| 项目 | 本次版本 / 配置 | 固定方式 |
|---|---|---|
| .NET SDK | 10.0.401 | 根目录 `global.json`，`rollForward=disable` |
| .NET 自包含运行时 | 10.0.12 / win-x64 | 本次 `GlassDesk.runtimeconfig.json` 的 `includedFrameworks`；隐式运行时包也纳入许可清单 |
| Windows App SDK | 2.5.1 | App `PackageReference` 与 `packages.lock.json` |
| WinUI 组件包 | 2.3.9 | Windows App SDK 的锁定传递依赖 |
| Windows SDK 原生工具链 | 10.0.26100.0 | 原生构建脚本检查所选 `WindowsSDKVersion` |
| Windows SDK .NET 投影包 | 10.0.26100.57 | .NET SDK 隐式依赖，见生成的 `.deps.json` |
| MSVC x64 | 14.51.36231，Visual Studio Community 2026 | 已安装 C++ 工作负载；原生脚本通过 `vswhere` 选择有 x64 工具的 Visual Studio |
| Inno Setup | 7.1.0 x64 | 已验证的独立编译器目录 `.tools\innosetup-7.1.0`，或传入 `-CompilerPath` |

首次准备开发机需安装以上 .NET SDK、带 x64 C++ 工具的有效许可 Visual Studio / Build Tools、Windows SDK 和 Inno Setup。遵守所选 Visual Studio 版本的使用及再分发条件。构建工具及 SDK 不安装到用户的 GlassDesk 目录。

原生脚本严格检查 Windows SDK 版本，但使用当前 Visual Studio 默认 MSVC 工具集；如需复现本次二进制，应同时保留 MSVC 14.51.36231。锁文件固定直接及传递 NuGet 依赖，首次还原需要能访问官方 NuGet 源。更新工具链或依赖后重新核对运行时版本及 [许可清单](../THIRD-PARTY-NOTICES.md)。

## 验证本次 Inno 编译器

本次 `.tools\innosetup-7.1.0\ISCC.exe` 的 Authenticode 状态为 `Valid`，签名主体为 `Pyrsys B.V.`。SHA-256 为：

```text
d06ebd38f38e3cee60a3c50cc45bd449d77e0bc6a5cabc607ea9886808e4de1a
```

在 GlassDesk 根目录检查：

```powershell
Get-AuthenticodeSignature -LiteralPath '.\.tools\innosetup-7.1.0\ISCC.exe'
Get-FileHash -LiteralPath '.\.tools\innosetup-7.1.0\ISCC.exe' -Algorithm SHA256
```

此值是已验证编译器文件的指纹，不是最终 GlassDesk 安装器的指纹。`.tools` 为本地构建工具目录；在不带该目录的源码副本中，从 [Inno Setup 官方页面](https://jrsoftware.org/isdl.php) 准备 7.1.0 x64，并保留编译器配套文件、语言文件和原始许可。可以将其编译器路径显式传给构建脚本。

## 一键 Release 构建

在 GlassDesk 根目录的 PowerShell 执行：

```powershell
.\scripts\build-release.ps1
```

使用另外准备的 Inno 7.1.0 编译器时：

```powershell
.\scripts\build-release.ps1 -CompilerPath 'C:\BuildTools\InnoSetup-7.1.0\ISCC.exe'
```

脚本依次构建采集子进程 `GlassDesk.CaptureWorker.exe`、原生 DLL 和隐藏原生回归程序，执行回归，锁定还原 App 与四个检查工程，以 -m:1 / BuildInParallel=false 运行 Core、Platform、PluginChecks 和 AudioChecks，发布自包含目录，复制使用说明和完整许可目录，最后编译安装器并生成安装器 SHA-256。任一步失败即停止。脚本构建产物，不执行 GlassDesk 安装器或启动应用。

本轮仅做正常运行检查，不进行压力测试，使用：

```powershell
.\scripts\build-release.ps1 -SkipStress
```

也可与 `-CompilerPath` 同时使用。`-SkipStress` 只编译原生 DLL，不构建或执行隐藏原生回归程序；Core 检查接收 `--no-stress`，跳过重复打断压力循环；Platform 检查、锁定还原、自包含发布、安装器和 SHA-256 生成照常执行。不传该开关时，仍执行完整原生回归与 Core 压力检查。跳过模式不生成新的 `regression-result.json`；已有同名文件属于此前运行，不能作为本轮证据。构建模式本身也不代表 GUI、安装或长期驻留验收通过。

App 项目显式把生成的 `GlassDesk.pri` 及所有编译 XAML 文件加入发布清单，并在发布结束检查 `GlassDesk.pri`、`App.xbf` 是否存在。当前 SDK 的 unpackaged 发布路径会遗漏应用自己的 PRI；仅有 SDK 自带的 `Microsoft.UI*.pri` 不够。首次打开设置的 GUI 验收必须针对 `artifacts\publish`，不能用完整的 bin 目录替代。[Windows App SDK 上游问题](https://github.com/microsoft/WindowsAppSDK/issues/6720)

| 产物 | 路径 |
|---|---|
| 原生桥 | `artifacts\native\Release\GlassDesk.Native.dll` |
| 液态背景采集子进程 | `artifacts\native\Release\GlassDesk.CaptureWorker.exe`，同时发布到应用根目录 |
| 隐藏原生回归结果（默认模式） | `artifacts\native\Release\regression-result.json` |
| 自包含应用 | `artifacts\publish\GlassDesk.exe` 和同目录全部依赖 |
| WinUI 应用资源 | `artifacts\publish\GlassDesk.pri`、`App.xbf`；未来增加的 XAML 文件保留相对目录 |
| 使用说明、许可 | `artifacts\publish\UserGuide.md`、`THIRD-PARTY-NOTICES.md`、`licenses\` |
| 当前用户安装器 | `artifacts\installer\GlassDesk-0.2.0-win-x64-setup.exe` |
| 安装器校验值 | `artifacts\installer\SHA256SUMS.txt` |

源码交付包在完成测试报告后生成：

```powershell
.\scripts\package-source.ps1
```

输出为 `artifacts\installer\GlassDesk-0.2.0-source.zip` 与同名 `.sha256`。该包保留源码、项目/依赖锁定、构建脚本、文档和许可；排除本机工具、运行产物、截图、配置测试目录以及 bin/obj。

本次统一交付使用 `scripts/finalize-release.ps1`：先将发布目录与 `verification/tested-binary-0.2.0.json` 的实机验收二进制比对，只更新使用说明并重编译安装器，不重编译已经驻留验收的程序；然后生成完整发布文件哈希、打源码包、逐个验证 ZIP 内文件且检查没有缺项，最后在 `artifacts/release/0.2.0` 汇总安装器、源码、说明、验收报告及两包共同的 SHA256SUMS。复用该脚本时必须传入本次新构建自己的验收快照；不能用旧快照替新程序背书。源码 ZIP 不包含它自己的哈希以避免循环引用，源码包哈希写在外部清单和 release-verification.json。安装验收后可用 `-SkipInstallerBuild` 只更新源码和报告；脚本先验证现有安装器哈希及使用说明未变化，保持已经安装验收的安装器字节。

安装器按当前用户安装到 `%LocalAppData%\Programs\GlassDesk`，不要求管理员权限；开始菜单入口默认创建，桌面快捷方式可选。覆盖升级通过自己的 `--quit` 消息请求驻留实例退出。用户配置独立存放于 `%LocalAppData%\GlassDesk`，升级与默认卸载保留它。

## 单独构建和检查

```powershell
.\scripts\build-native.ps1 -Configuration Release -BuildRegression
.\artifacts\native\Release\GlassDesk.NativeRegression.exe
dotnet restore .\tests\GlassDesk.Core.Tests\GlassDesk.Core.Tests.csproj --locked-mode
dotnet run --project .\tests\GlassDesk.Core.Tests\GlassDesk.Core.Tests.csproj -c Release --no-restore
dotnet restore .\tests\GlassDesk.PlatformChecks\GlassDesk.PlatformChecks.csproj --locked-mode
dotnet run --project .\tests\GlassDesk.PlatformChecks\GlassDesk.PlatformChecks.csproj -c Release --no-restore
dotnet restore .\src\GlassDesk.App\GlassDesk.App.csproj --locked-mode
dotnet publish .\src\GlassDesk.App\GlassDesk.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o .\artifacts\publish -p:PublishSingleFile=false -p:PublishTrimmed=false
```

隐藏回归不改动 Windows 显示缩放；Platform 检查使用测试配置目录，不设置用户开机启动、快捷键或播放状态。单独 `dotnet publish` 不负责拷贝本文列出的 GlassDesk 使用说明及 `licenses`，正式安装器应使用完整 Release 脚本。

仅运行普通检查时，可单独执行以下命令，不调用原生回归程序：

```powershell
.\scripts\build-native.ps1 -Configuration Release
dotnet restore .\tests\GlassDesk.Core.Tests\GlassDesk.Core.Tests.csproj --locked-mode
dotnet run --project .\tests\GlassDesk.Core.Tests\GlassDesk.Core.Tests.csproj -c Release --no-restore -- --no-stress
dotnet restore .\tests\GlassDesk.PlatformChecks\GlassDesk.PlatformChecks.csproj --locked-mode
dotnet run --project .\tests\GlassDesk.PlatformChecks\GlassDesk.PlatformChecks.csproj -c Release --no-restore
```

原生 Release 使用 C++20、`/W4 /WX /O2 /MT`。`GlassDesk.NativeBridge` 将原生 DLL 和采集子进程拷贝至托管输出及发布目录；先执行原生构建。`/MT` 将本项目原生组件的 CRT 静态链接；其他 SDK 自带 DLL 保持其原有依赖，不手工剔除。

液态玻璃另有显式 QA 命令：`GlassDesk.NativeRegression.exe --liquid-hidden <绝对PNG路径>` 验证真实 WGC 与同帧材质输出，`--liquid-cycles 100` 验证每次真实采集和退出，`--capture-owner-exit` 验证宿主异常退出后子进程结束。这些命令不显示测试窗口、不投递桌面输入；不能代替可见安装版的 GUI 验收。`scripts/check-liquid-gui.ps1` 使用 cua-driver 对已经打开的设置页执行指定次数的真实开关验证，`scripts/observe-resident.ps1` 仅采集明确指定 PID 的资源计数。

App 配置同时启用 `WindowsAppSDKSelfContained` 和 .NET `SelfContained`；`WindowsPackageType=None`，`PublishTrimmed=false`，`PublishSingleFile=false`。WinUI 的自包含发布保留独立本机运行时文件，使用整个发布目录构建安装器。[微软自包含部署说明](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)

## 许可再生成和交付检查

首次还原后，本地安装的 Windows SDK 和 Inno 文件就绪时，可执行：

```powershell
python .\licenses\collect_licenses.py
```

该辅助脚本仅写 `licenses`，复制官方 NuGet 缓存中的许可、声明及 nuspec，保存 Windows SDK 与 Inno 原文；缺少的 C#/WinRT、Visual C++ / Visual Studio 许可从固定官方地址下载。Python 仅用于维护许可清单，普通 Release 构建使用已经保存的许可目录。原始 DOCX 与可阅读的文本提取并存，`manifest.json` 记录来源和 SHA-256。

正式交付同时保留依赖 DLL、运行时配置、`THIRD-PARTY-NOTICES.md` 和完整 `licenses` 目录。Inno `LicenseFile` 显示随附组件条款。不要用源仓库的 MIT 许可替代 Windows App SDK 二进制包附带的微软许可。许可版本与实际 `.deps.json`、锁文件不一致时，先重新生成清单再打包。

当前原型未签署 GlassDesk 自身 EXE / 安装器；Inno 编译器的有效签名不等于生成的安装器已有签名。实际系统提示、安装和卸载测试结果由 [测试报告](test-report.md) 记录。


