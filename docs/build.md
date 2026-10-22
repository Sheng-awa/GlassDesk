# 构建与检查

当前应用版本由根目录 `Directory.Build.props` 决定，为 **0.1.0**。不要使用旧内部预览编号推断发布版本。最低声明为 Windows 11 Build 22621 x64；声明最低版本不等于已在干净机器验证。

## 工具链

- Windows x64 开发环境。
- .NET SDK **10.0.401**，见 `global.json`。还原使用已提交的锁文件。
- Visual Studio 的 MSVC x64 C++ 工具；脚本通过 `vswhere` 查找。
- Windows SDK **10.0.26100.0**；原生脚本校验所选 SDK。
- 仅生成安装包时需要 Inno Setup **7.1.0**；可用 `-CompilerPath` 传入编译器。

主要依赖与原始许可保存在 [第三方声明](../THIRD-PARTY-NOTICES.md) 和 `licenses` 中。不要用项目 MIT 许可替换微软二进制包的许可。

## 自包含编译

从仓库根目录执行：

```powershell
.\scripts\build-native.ps1 -Configuration Release
dotnet restore .\src\GlassDesk.App\GlassDesk.App.csproj --locked-mode
dotnet publish .\src\GlassDesk.App\GlassDesk.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:GlassDeskUiPreview=false -p:PublishSingleFile=false -p:PublishTrimmed=false -o .\artifacts\publish
```

原生步骤会生成宿主 DLL 与捕获子进程。WinUI 发布保留完整资源与运行依赖，不要仅复制一个 EXE。

## 普通检查

以下为控制台检查程序，使用 `dotnet run` 才会执行断言。`--no-stress` 关闭 Core / Plugin 的循环压力部分，Platform 忽略该参数。

```powershell
dotnet run --project tests/GlassDesk.Core.Tests -c Release -- --no-stress
dotnet run --project tests/GlassDesk.PlatformChecks -c Release -- --no-stress
dotnet run --project tests/GlassDesk.PluginChecks -c Release -- --no-stress
```

CI 执行原生编译、锁定还原、自包含发布和以上隔离检查；不安装应用、不操作桌面输入或真实音乐，也不自动发布安装包。首次运行结果以 [Actions](https://github.com/Sheng-awa/GlassDesk/actions) 为准，不预先宣称成功。

工作流使用官方 [checkout](https://github.com/actions/checkout) 与 [setup-dotnet](https://github.com/actions/setup-dotnet)，按已核实提交固定版本；仓库权限为只读。参考 [GitHub 工作流语法](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)。

## 安装包

```powershell
.\scripts\build-release.ps1 -CompilerPath '工具目录\ISCC.exe' -SkipStress
```

该脚本会编译、运行检查并打包；包含音频检查，因此需要合适的 Windows 环境，不等同于上面的最小 CI。运行前先审阅脚本及输出目录。发布使用完整运行依赖、许可证、用户说明和静态展示文件。历史内部预览 / 冻结批次脚本不是当前 0.1 的发布入口。

公开安装包保留为首次发布的既有文件；主分支的文档勘误并不表示已替换该安装包或移动标签。当前发布摘要与验证边界见 [发布记录](../verification/release-0.1.0.json) 和 [已知问题](known-issues.md)。
