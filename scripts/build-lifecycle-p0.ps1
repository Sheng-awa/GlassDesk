$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$taskVs=& $taskVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
& (Join-Path $taskVs 'Common7\Tools\Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
$taskOutput=Join-Path $taskRoot 'artifacts\p0\lifecycle'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
Push-Location -LiteralPath $taskOutput
try {
  & cl.exe /nologo /std:c++20 /EHsc /W4 /O2 /MT /LD /DGLASSDESK_P0_DLL /DUNICODE /D_UNICODE (Join-Path $taskRoot 'native\P0\GlassHost.cpp') /Fe:GlassDesk.ProbeNative.dll /link user32.lib gdi32.lib dwmapi.lib windowsapp.lib CoreMessaging.lib dxguid.lib d2d1.lib
  if($LASTEXITCODE -ne 0){throw 'Lifecycle native build failed.'}
} finally {Pop-Location}
