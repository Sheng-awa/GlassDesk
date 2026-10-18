param([ValidateSet('Release','Debug')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\','/')
$taskOutput=[IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts/native-ui-preview/0.3.0-preview.2/typography-probe'))
if(!$taskOutput.StartsWith($taskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Probe output escaped GlassDesk.'}
$taskAncestor=$taskOutput
while($taskAncestor -and ![StringComparer]::OrdinalIgnoreCase.Equals($taskAncestor,$taskRoot)){
    if((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Probe output must not traverse a reparse point.'}
    $taskAncestor=[IO.Path]::GetDirectoryName($taskAncestor)
}
$taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$taskVs=& $taskVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$taskVs){throw 'MSVC x64 is required.'}
& (Join-Path $taskVs 'Common7/Tools/Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
if($env:WindowsSDKVersion.TrimEnd('\') -ne '10.0.26100.0'){throw 'Windows SDK 10.0.26100.0 is required; do not upgrade the project SDK.'}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskOptimize=if($Configuration -eq 'Release'){'/O2'}else{'/Od'}
Push-Location -LiteralPath $taskOutput
try {
    & cl.exe /nologo /std:c++20 /EHsc /W4 /WX /FC /diagnostics:column $taskOptimize /MT /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0A00 /permissive- /utf-8 (Join-Path $taskRoot 'native/GlassDesk.TypographyProbe/TypographyProbe.cpp') (Join-Path $taskRoot 'native/GlassDesk.Native/TypographyEngine.cpp') /Fe:GlassDesk.TypographyProbe.exe /link /SUBSYSTEM:CONSOLE /DYNAMICBASE /NXCOMPAT /guard:cf d2d1.lib dwrite.lib windowscodecs.lib ole32.lib runtimeobject.lib windowsapp.lib
    if($LASTEXITCODE -ne 0){throw 'GlassDesk typography probe build failed.'}
} finally {Pop-Location}
