param([string]$OutputName='native')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$taskVs = & $taskVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $taskVs) { throw 'MSVC x64 toolchain is required.' }
$taskDevShell = Join-Path $taskVs 'Common7\Tools\Launch-VsDevShell.ps1'
& $taskDevShell -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
if ($OutputName -notmatch '^[A-Za-z0-9-]+$') { throw 'OutputName must be a simple directory name.' }
$taskOutput = Join-Path $taskRoot "artifacts\p0\$OutputName"
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
Push-Location -LiteralPath $taskOutput
try {
    & cl.exe /nologo /std:c++20 /EHsc /W4 /O2 /MT /DUNICODE /D_UNICODE (Join-Path $taskRoot 'native\P0\GlassHost.cpp') /Fe:GlassDesk.GlassHostP0.exe /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib dwmapi.lib windowsapp.lib CoreMessaging.lib dxguid.lib d2d1.lib
    if ($LASTEXITCODE -ne 0) { throw 'Native P0 build failed.' }
    Copy-Item -LiteralPath 'GlassDesk.GlassHostP0.exe' -Destination 'GlassDesk.ClickReceiverP0.exe'
} finally { Pop-Location }
