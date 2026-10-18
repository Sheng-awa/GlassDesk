param([string]$Configuration='Release',[switch]$BuildRegression,[string]$OutputDirectory='')
$ErrorActionPreference='Stop'
if ($Configuration -notin @('Release','Debug')) { throw 'Configuration must be Release or Debug.' }
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskOutput=Join-Path $taskRoot "artifacts\native\$Configuration"
if($OutputDirectory){
    $taskRequestedOutput=if([IO.Path]::IsPathRooted($OutputDirectory)){$OutputDirectory}else{Join-Path $taskRoot $OutputDirectory}
    $taskRequestedOutput=[IO.Path]::GetFullPath($taskRequestedOutput).TrimEnd('\','/')
    $taskAllowedOutputs=@('0.3.0-preview.1','0.3.0-preview.2') | ForEach-Object {
        [IO.Path]::GetFullPath((Join-Path $taskRoot "artifacts/native-ui-preview/$_")).TrimEnd('\','/')
    }
    if(!($taskAllowedOutputs | Where-Object {[StringComparer]::OrdinalIgnoreCase.Equals($taskRequestedOutput,$_)})){throw 'OutputDirectory must be an exact GlassDesk preview native directory.'}
    $taskOutputParent=$taskRequestedOutput
    while($taskOutputParent -and ![StringComparer]::OrdinalIgnoreCase.Equals($taskOutputParent,$taskRoot)){
        if((Test-Path -LiteralPath $taskOutputParent) -and ((Get-Item -LiteralPath $taskOutputParent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Preview native output must not pass through a reparse point.'}
        $taskOutputParent=[IO.Path]::GetDirectoryName($taskOutputParent)
    }
    $taskOutput=$taskRequestedOutput
}
$taskVsWhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$taskVs=& $taskVsWhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(-not $taskVs){throw 'MSVC x64 is required.'}
& (Join-Path $taskVs 'Common7\Tools\Launch-VsDevShell.ps1') -Arch amd64 -HostArch amd64 -SkipAutomaticLocation
if($env:WindowsSDKVersion.TrimEnd('\') -ne '10.0.26100.0'){throw 'Windows SDK 10.0.26100.0 must be selected in the developer environment.'}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskOptimize=if($Configuration -eq 'Release'){'/O2'}else{'/Od'}
Push-Location -LiteralPath $taskOutput
try {
    & cl.exe /nologo /std:c++20 /EHsc /W4 /WX $taskOptimize /MT /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0A00 /permissive- /utf-8 (Join-Path $taskRoot 'native\GlassDesk.Native\CaptureWorker.cpp') /Fe:GlassDesk.CaptureWorker.exe /link /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT /guard:cf user32.lib windowsapp.lib dxgi.lib d3d11.lib ole32.lib shell32.lib
    if($LASTEXITCODE -ne 0){throw 'GlassDesk capture worker build failed.'}
    & cl.exe /nologo /std:c++20 /EHsc /W4 /WX $taskOptimize /MT /LD /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0A00 /permissive- /utf-8 (Join-Path $taskRoot 'native\GlassDesk.Native\GlassDeskNative.cpp') (Join-Path $taskRoot 'native\GlassDesk.Native\ProcessAudio.cpp') (Join-Path $taskRoot 'native\GlassDesk.Native\LiquidGlass.cpp') (Join-Path $taskRoot 'native\GlassDesk.Native\TypographyEngine.cpp') /Fe:GlassDesk.Native.dll /link /DYNAMICBASE /NXCOMPAT /guard:cf user32.lib gdi32.lib dwmapi.lib windowsapp.lib CoreMessaging.lib dxguid.lib d2d1.lib d3d11.lib dwrite.lib windowscodecs.lib ole32.lib oleaut32.lib uiautomationcore.lib mmdevapi.lib d3dcompiler.lib
    if($LASTEXITCODE -ne 0){throw 'GlassDesk.Native build failed.'}
    if($BuildRegression) {
        & cl.exe /nologo /std:c++20 /EHsc /W4 /WX $taskOptimize /MT /DUNICODE /D_UNICODE /D_WIN32_WINNT=0x0A00 /permissive- /utf-8 (Join-Path $taskRoot 'native\GlassDesk.Native\NativeRegression.cpp') (Join-Path $taskRoot 'native\GlassDesk.Native\LiquidGlass.cpp') (Join-Path $taskRoot 'native\GlassDesk.Native\TypographyEngine.cpp') /Fe:GlassDesk.NativeRegression.exe /link /SUBSYSTEM:CONSOLE /DYNAMICBASE /NXCOMPAT /guard:cf user32.lib gdi32.lib dwmapi.lib windowsapp.lib CoreMessaging.lib dxguid.lib d2d1.lib d3d11.lib dwrite.lib windowscodecs.lib ole32.lib oleaut32.lib uiautomationcore.lib mmdevapi.lib d3dcompiler.lib
        if($LASTEXITCODE -ne 0){throw 'GlassDesk native regression fixture build failed.'}
    }
} finally {Pop-Location}

