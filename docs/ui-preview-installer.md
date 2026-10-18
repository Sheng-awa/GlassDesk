# UI预览安装器

运行 `scripts/build-ui-preview.ps1` 统一编译程序、执行检查，并生成便携运行ZIP、源码ZIP及当前用户安装器。默认编译器为 `.tools/innosetup-7.1.0/ISCC.exe`，源码副本可用 `-CompilerPath` 指定独立准备的同版编译器。

安装模板为 `installer/GlassDesk.UiPreview.iss`。预览安装AppId为A516715C-9FE6-4FE6-BD8B-B07BC474986D，与正式0.2.0分开。默认目录为当前用户Programs下的GlassDesk UI Preview；开始菜单和可选桌面快捷方式使用独立名称。安装器拒绝覆盖默认正式目录或已识别的0.2.0程序目录。

程序编译为预览模式，快捷方式仍显式提供 `--ui-preview`。配置位于安装目录的preview-data，实例IPC由规范化配置路径的UTF8 SHA256派生。升级和卸载只请求退出该配置路径的预览实例并等待互斥量消失，不退出其他预览或正式实例。卸载保留用户创建的preview-data，不修改正式启动项或正式配置。

安装器不嵌入最终AcceptanceReport.md；该报告随外部发布目录和便携ZIP提供。驻留结束后 `scripts/finalize-ui-preview.py` 只重封装文档与源码，并验证程序、安装器和正式0.2.0基线字节未变化。SHA256SUMS同时覆盖源码ZIP、便携ZIP和安装器。

预览安装器的存在不代表干净机器、正式0.2升级迁移或所有系统环境已验收。当前可执行的安装、覆盖升级、卸载与配置保留证据统一记录在ui-acceptance-0.3.0-preview.1.md。
