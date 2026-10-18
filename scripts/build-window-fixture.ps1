param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$taskVs=& $taskVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $taskVs){throw 'MSVC x64 is required.'}
& (Join-Path $taskVs 'Common7\Tools\Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
$taskOutput=if($OutputDirectory){[IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))}else{Join-Path $taskRoot 'artifacts\qa\window-fixture'}
if(-not $taskOutput.StartsWith($taskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture output must be inside GlassDesk.'}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
Push-Location -LiteralPath $taskOutput
try {
    & cl.exe /nologo /std:c++20 /EHsc /W4 /WX /O2 /MT /DUNICODE /D_UNICODE /permissive- /utf-8 (Join-Path $taskRoot 'tools\GlassDesk.WindowFixture\main.cpp') /Fe:GlassDesk.WindowFixture.exe /link /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT user32.lib gdi32.lib
    if($LASTEXITCODE -ne 0){throw 'Window fixture build failed.'}
} finally {Pop-Location}
