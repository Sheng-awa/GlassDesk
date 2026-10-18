param([string]$Configuration='Release')
$ErrorActionPreference='Stop'
if($Configuration -notin @('Release','Debug')){throw 'Configuration must be Release or Debug.'}
$captureQaRoot=Split-Path -Parent $PSScriptRoot
$captureQaVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$captureQaVs=& $captureQaVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $captureQaVs){throw 'MSVC x64 is required.'}
& (Join-Path $captureQaVs 'Common7\Tools\Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
if($env:WindowsSDKVersion.TrimEnd('\') -ne '10.0.26100.0'){throw 'Windows SDK 10.0.26100.0 must be selected in the developer environment.'}
$captureQaOutput=Join-Path $captureQaRoot "artifacts\capture-diagnostics\$Configuration"
New-Item -ItemType Directory -Path $captureQaOutput -Force | Out-Null
$captureQaOptimize=if($Configuration -eq 'Release'){'/O2'}else{'/Od'}
Push-Location -LiteralPath $captureQaOutput
try {
    & cl.exe /nologo /std:c++20 /EHsc /W4 /WX $captureQaOptimize /MT /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0A00 /permissive- /utf-8 (Join-Path $captureQaRoot 'native\GlassDesk.Native\CaptureDiagnostic.cpp') /Fe:GlassDesk.CaptureDiagnostic.exe /link /SUBSYSTEM:CONSOLE /DYNAMICBASE /NXCOMPAT user32.lib d3d11.lib dxgi.lib windowsapp.lib ole32.lib
    if($LASTEXITCODE -ne 0){throw 'Standalone WGC diagnostic build failed.'}
    & cl.exe /nologo /std:c++20 /EHsc /W4 /WX $captureQaOptimize /MT /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0A00 /permissive- /utf-8 (Join-Path $captureQaRoot 'native\GlassDesk.Native\PresentationProbe.cpp') /Fe:GlassDesk.PresentationProbe.exe /link /SUBSYSTEM:CONSOLE /DYNAMICBASE /NXCOMPAT user32.lib d3d11.lib dxgi.lib windowsapp.lib ole32.lib
    if($LASTEXITCODE -ne 0){throw 'Desktop presentation ROI probe build failed.'}
} finally {Pop-Location}
