[CmdletBinding()]
param([string]$CompilerPath='')

$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$taskRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\','/')
$taskVersion='0.3.0-preview.1'
$taskNative=Join-Path $taskRoot "artifacts/native-ui-preview/$taskVersion"
$taskPublish=Join-Path $taskRoot "artifacts/ui-preview/$taskVersion"
$taskData=Join-Path $taskRoot "artifacts/ui-preview-data/$taskVersion"
$taskQa=Join-Path $taskRoot "artifacts/qa/ui-$taskVersion"
$taskRelease=Join-Path $taskRoot "artifacts/release/$taskVersion"
$taskFormal=Join-Path $taskRoot 'artifacts/release/0.2.0'
if(!$CompilerPath){$CompilerPath=Join-Path $taskRoot '.tools/innosetup-7.1.0/ISCC.exe'}
if(!(Test-Path -LiteralPath $CompilerPath -PathType Leaf)){throw 'Inno Setup compiler not found; pass -CompilerPath.'}

function Assert-PreviewPath([string]$Path,[string]$Expected){
    $taskFull=[IO.Path]::GetFullPath($Path).TrimEnd('\','/')
    $taskExact=[IO.Path]::GetFullPath($Expected).TrimEnd('\','/')
    if(![StringComparer]::OrdinalIgnoreCase.Equals($taskFull,$taskExact) -or !$taskFull.StartsWith($taskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw "Unsafe preview output: $Path"}
    $taskAncestor=$taskFull
    while(![StringComparer]::OrdinalIgnoreCase.Equals($taskAncestor,$taskRoot)){
        if((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Preview output passes through a reparse point: $taskAncestor"}
        $taskAncestor=[IO.Path]::GetDirectoryName($taskAncestor)
        if(!$taskAncestor){throw "Preview output escaped GlassDesk: $Path"}
    }
    return $taskFull
}

function Get-FileManifest([string]$Root,[System.IO.FileInfo[]]$Files){
    $taskHashes=[ordered]@{}
    foreach($taskFile in ($Files | Sort-Object FullName)){
        $taskRelative=[IO.Path]::GetRelativePath($Root,$taskFile.FullName).Replace('\','/')
        $taskHashes[$taskRelative]=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return ,$taskHashes
}

function Get-SourceFiles {
    $taskPending=[Collections.Generic.Stack[string]]::new()
    $taskFound=[Collections.Generic.List[IO.FileInfo]]::new()
    $taskPending.Push($taskRoot)
    while($taskPending.Count){
        foreach($taskEntry in (Get-ChildItem -LiteralPath $taskPending.Pop() -Force)){
            if($taskEntry.PSIsContainer -and $taskEntry.Name -in @('artifacts','.tools','bin','obj','.git','.vs','__pycache__')){continue}
            if($taskEntry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Source snapshot refuses a reparse point: $($taskEntry.FullName)"}
            if($taskEntry.PSIsContainer){$taskPending.Push($taskEntry.FullName)}
            elseif($taskEntry.Extension -ne '.user'){$taskFound.Add($taskEntry)}
        }
    }
    return $taskFound.ToArray()
}

function Invoke-PreviewTool([string]$FilePath,[string[]]$Arguments,[string]$LogName){
    $taskLog=Join-Path $taskQa $LogName
    & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $taskLog
    if($LASTEXITCODE -ne 0){throw "Preview command failed ($LASTEXITCODE): $FilePath. See $taskLog"}
}

function New-VerifiedZip([string]$Path,[string]$Root,[IO.FileInfo[]]$Files){
    $taskZipStream=[IO.File]::Open($Path,[IO.FileMode]::Create,[IO.FileAccess]::Write)
    $taskZip=[IO.Compression.ZipArchive]::new($taskZipStream,[IO.Compression.ZipArchiveMode]::Create)
    try{
        foreach($taskFile in ($Files | Sort-Object FullName)){
            $taskEntryName=[IO.Path]::GetRelativePath($Root,$taskFile.FullName).Replace('\','/')
            if($taskEntryName.StartsWith('../',[StringComparison]::Ordinal)){throw 'Archive entry escaped its root.'}
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip,$taskFile.FullName,$taskEntryName,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }finally{$taskZip.Dispose();$taskZipStream.Dispose()}
    $taskZip=[IO.Compression.ZipFile]::OpenRead($Path)
    $taskEntryNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    try{
        foreach($taskEntry in $taskZip.Entries){
            if(!$taskEntryNames.Add($taskEntry.FullName)){throw 'Duplicate archive entry.'}
            $taskSourceFile=[IO.Path]::GetFullPath((Join-Path $Root $taskEntry.FullName))
            if(!$taskSourceFile.StartsWith($Root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Archive verification escaped its root.'}
            $taskEntryStream=$taskEntry.Open()
            try{$taskEntryHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskEntryStream)).ToLowerInvariant()}finally{$taskEntryStream.Dispose()}
            if($taskEntryHash -ne (Get-FileHash -LiteralPath $taskSourceFile -Algorithm SHA256).Hash.ToLowerInvariant()){throw "Archive byte mismatch: $($taskEntry.FullName)"}
        }
        if($taskEntryNames.Count -ne $Files.Count){throw 'Archive entry count mismatch.'}
    }finally{$taskZip.Dispose()}
}

foreach($taskExpected in @($taskNative,$taskPublish,$taskData,$taskQa,$taskRelease)){
    [void](Assert-PreviewPath $taskExpected $taskExpected)
    New-Item -ItemType Directory -Path $taskExpected -Force | Out-Null
}
if(!(Test-Path -LiteralPath $taskFormal -PathType Container)){throw 'The protected 0.2.0 release baseline is missing.'}
$taskFormalBefore=Get-FileManifest $taskFormal @(Get-ChildItem -LiteralPath $taskFormal -File -Recurse -Force)
$taskFormalJson=$taskFormalBefore | ConvertTo-Json -Compress
$taskFormalBefore | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskQa 'preview-build-formal-baseline.json') -Encoding utf8
$taskSourceFiles=@(Get-SourceFiles)
$taskSourceBefore=Get-FileManifest $taskRoot $taskSourceFiles
$taskSourceJson=$taskSourceBefore | ConvertTo-Json -Compress
$taskStarted=[DateTimeOffset]::UtcNow.ToString('O')

Push-Location -LiteralPath $taskRoot
try{
    $taskLivePreview=@(Get-Process -Name GlassDesk -ErrorAction SilentlyContinue | Where-Object {
        try {[StringComparer]::OrdinalIgnoreCase.Equals($_.Path,(Join-Path $taskPublish 'GlassDesk.exe'))}catch{$false}
    })
    if($taskLivePreview.Count){throw 'This preview runtime is still running. Exit only its own preview instance before rebuilding; output was not cleaned.'}
    # Clean only this preview's generated runtime files. Preserve any portable
    # preview-data alongside the executable and the fixed development data root.
    foreach($taskOldOutput in (Get-ChildItem -LiteralPath $taskPublish -Force)){
        if($taskOldOutput.Name -eq 'preview-data'){continue}
        $taskRemove=[IO.Path]::GetFullPath($taskOldOutput.FullName)
        if(!$taskRemove.StartsWith($taskPublish+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Refusing to clean outside the exact preview runtime directory.'}
        [void](Assert-PreviewPath $taskRemove $taskRemove)
        Remove-Item -LiteralPath $taskRemove -Recurse -Force
    }
    $taskSdk=& dotnet --version
    if($LASTEXITCODE -ne 0 -or $taskSdk.Trim() -ne '10.0.401'){throw 'The pinned .NET SDK 10.0.401 is required.'}
    $taskSdk | Set-Content -LiteralPath (Join-Path $taskQa 'preview-build-sdk.txt') -Encoding utf8
    & (Join-Path $PSScriptRoot 'build-native.ps1') -Configuration Release -BuildRegression -OutputDirectory $taskNative 2>&1 | Tee-Object -FilePath (Join-Path $taskQa 'preview-build-native.log')
    if($LASTEXITCODE -ne 0){throw 'Preview native build failed. See preview-build-native.log.'}
    Invoke-PreviewTool -FilePath (Join-Path $taskNative 'GlassDesk.NativeRegression.exe') -Arguments @() -LogName 'preview-build-native-hidden.log'
    $taskNativeProperty="-p:GlassDeskNativeDirectory=$taskNative"
    $taskVersionProperty="-p:Version=$taskVersion"
    $taskSerial=@('-m:1','-p:BuildInParallel=false')
    Invoke-PreviewTool 'dotnet' (@('restore','./src/GlassDesk.App/GlassDesk.App.csproj','--locked-mode',$taskNativeProperty,$taskVersionProperty,'-p:GlassDeskUiPreview=true')+$taskSerial) 'preview-restore-app.log'
    $taskLegacyFixture='./tests/Fixtures/LegacyPlugin/GlassDesk.LegacyPlugin.csproj'
    Invoke-PreviewTool 'dotnet' (@('restore',$taskLegacyFixture,'--locked-mode')+$taskSerial) 'preview-restore-legacy-plugin.log'
    Invoke-PreviewTool 'dotnet' (@('build',$taskLegacyFixture,'-c','Release','--no-restore')+$taskSerial) 'preview-build-legacy-plugin.log'
    foreach($taskCheck in @('GlassDesk.Core.Tests','GlassDesk.PlatformChecks','GlassDesk.PluginChecks','GlassDesk.AudioChecks')){
        $taskProject="./tests/$taskCheck/$taskCheck.csproj"
        Invoke-PreviewTool 'dotnet' (@('restore',$taskProject,'--locked-mode',$taskNativeProperty)+$taskSerial) "preview-restore-$taskCheck.log"
        Invoke-PreviewTool 'dotnet' (@('build',$taskProject,'-c','Release','--no-restore',$taskNativeProperty)+$taskSerial) "preview-build-$taskCheck.log"
        Invoke-PreviewTool 'dotnet' @('run','--project',$taskProject,'-c','Release','--no-build','--no-restore') "preview-check-$taskCheck.log"
    }
    Invoke-PreviewTool 'dotnet' (@('publish','./src/GlassDesk.App/GlassDesk.App.csproj','-c','Release','-r','win-x64','--self-contained','true','--no-restore','-p:GlassDeskUiPreview=true',$taskVersionProperty,$taskNativeProperty,'-p:PublishSingleFile=false','-p:PublishTrimmed=false','-o',$taskPublish)+$taskSerial) 'preview-publish.log'

    foreach($taskRequired in @('GlassDesk.exe','GlassDesk.dll','GlassDesk.Core.dll','GlassDesk.Platform.dll','GlassDesk.NativeBridge.dll','GlassDesk.Plugins.dll','GlassDesk.Native.dll','GlassDesk.CaptureWorker.exe','GlassDesk.pri','App.xbf','GlassDesk.deps.json','GlassDesk.runtimeconfig.json','WinRT.Runtime.dll','Microsoft.ui.xaml.dll','Microsoft.UI.pri','Microsoft.WindowsAppRuntime.dll','Microsoft.WindowsAppRuntime.Bootstrap.dll','coreclr.dll','hostfxr.dll','hostpolicy.dll')){
        $taskRequiredFile=Join-Path $taskPublish $taskRequired
        if(!(Test-Path -LiteralPath $taskRequiredFile -PathType Leaf) -or (Get-Item -LiteralPath $taskRequiredFile).Length -le 0){throw "Incomplete UI Preview runtime: $taskRequired"}
    }
    [void](Get-Content -LiteralPath (Join-Path $taskPublish 'GlassDesk.deps.json') -Raw | ConvertFrom-Json)
    [void](Get-Content -LiteralPath (Join-Path $taskPublish 'GlassDesk.runtimeconfig.json') -Raw | ConvertFrom-Json)
    foreach($taskNativeFile in @('GlassDesk.Native.dll','GlassDesk.CaptureWorker.exe')){
        if((Get-FileHash -LiteralPath (Join-Path $taskNative $taskNativeFile)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $taskPublish $taskNativeFile)).Hash){throw "Publish used the wrong native output: $taskNativeFile"}
    }
    $taskProductVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $taskPublish 'GlassDesk.dll')).ProductVersion
    if(!$taskProductVersion.StartsWith($taskVersion,[StringComparison]::Ordinal)){throw "Wrong preview product version: $taskProductVersion"}
    Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination $taskPublish
    Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY-NOTICES.md') -Destination $taskPublish
    Copy-Item -LiteralPath (Join-Path $taskRoot 'licenses') -Destination (Join-Path $taskPublish 'licenses') -Recurse
    if(Test-Path -LiteralPath (Join-Path $taskRoot 'docs/ui-acceptance-0.3.0-preview.1.md')){
        Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/ui-acceptance-0.3.0-preview.1.md') -Destination (Join-Path $taskPublish 'AcceptanceReport.md')
    }
    $taskPortableLauncher=@'
[CmdletBinding()]
param([string]$DataDirectory='',[switch]$Quit)
$ErrorActionPreference='Stop'
if(!$DataDirectory){$DataDirectory=Join-Path $PSScriptRoot 'preview-data'}
$previewData=[IO.Path]::GetFullPath($DataDirectory)
New-Item -ItemType Directory -Path $previewData -Force | Out-Null
$previewArguments=@('--ui-preview',"--data-dir=$previewData")
if($Quit){$previewArguments+='--quit'}
& (Join-Path $PSScriptRoot 'GlassDesk.exe') @previewArguments
'@
    Set-Content -LiteralPath (Join-Path $taskPublish 'Start-GlassDeskUiPreview.ps1') -Value $taskPortableLauncher -Encoding utf8
    $taskDevelopmentLauncher=@'
[CmdletBinding()]
param([switch]$Quit)
$ErrorActionPreference='Stop'
$previewData=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../ui-preview-data/0.3.0-preview.1'))
$previewLauncher=Join-Path $PSScriptRoot '0.3.0-preview.1/Start-GlassDeskUiPreview.ps1'
& $previewLauncher -DataDirectory $previewData -Quit:$Quit
'@
    Set-Content -LiteralPath (Join-Path (Split-Path -Parent $taskPublish) 'Start-GlassDeskUiPreview.ps1') -Value $taskDevelopmentLauncher -Encoding utf8
    $taskReadme=@'
# GlassDesk 0.3.0-preview.1 · UI Preview

This portable preview implements UI and interaction foundations. Focus, notes,
and other new content features remain for the next stage. The five Details
navigation items preview selection while the music content remains visible.

Extract the runtime ZIP completely into a writable directory. In PowerShell,
run ./Start-GlassDeskUiPreview.ps1. The launcher explicitly supplies --ui-preview
and --data-dir, using ./preview-data beside the extracted application. To use a
different isolated directory, pass -DataDirectory <absolute-directory>. To exit
that same preview instance, use the same directory and add -Quit. The binary is
also compiled in preview mode; system startup and global shortcut changes are
disabled. A current-user preview installer is supplied separately. It uses its
own application identity and directory; the formal 0.2.0 release is unchanged.
The installer retains ./preview-data when uninstalling. AcceptanceReport.md is
distributed beside the installer and inside the portable ZIP, not embedded in
the installer, so final acceptance updates do not change tested installer bytes.

In the source checkout, artifacts/ui-preview/Start-GlassDeskUiPreview.ps1 uses
artifacts/ui-preview-data/0.3.0-preview.1. No existing preview data, personal song
history, accounts, formal configuration, or generated test data is included in
the ZIP. Windows 11 x64 is required; the runtime is self-contained and must stay
together with its PRI, XBF, native DLL, capture worker, and dependency files.

The optional liquid material remains off by default and uses the existing WGC
worker and GPU path. Capture exclusion can hide the widget in external screen
captures. Material diagnostics and final UI screenshots are different evidence.

Automated build/check logs are recorded separately under artifacts/qa. Building
or packaging does not prove native pointer behavior, appearance on three desktop
backgrounds, resource residency, real mixed DPI, sleep, or device removal. Consult
AcceptanceReport.md when supplied; any unmeasured or failing cases stay explicit.
release-verification.json links the exact source ZIP and runtime files by SHA-256.
The included third-party inventory originated with 0.2.0; dependencies are unchanged.
'@
    Set-Content -LiteralPath (Join-Path $taskPublish 'UI-PREVIEW-README.md') -Value $taskReadme -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $taskPublish 'UI-PREVIEW-README.md') -Destination (Join-Path $taskRelease 'UI-PREVIEW-README.md') -Force

    $taskSourceFiles=@(Get-SourceFiles)
    $taskSourceAfter=Get-FileManifest $taskRoot $taskSourceFiles
    if(($taskSourceAfter | ConvertTo-Json -Compress) -ne $taskSourceJson){throw 'Source changed during the build. Rebuild from a stable source snapshot before packaging.'}
    $taskSourceZip=Join-Path $taskRelease "GlassDesk-$taskVersion-source.zip"
    $taskRuntimeZip=Join-Path $taskRelease "GlassDesk-$taskVersion-win-x64-ui-preview.zip"
    New-VerifiedZip $taskSourceZip $taskRoot $taskSourceFiles
    $taskRuntimeFiles=@(Get-ChildItem -LiteralPath $taskPublish -File -Recurse -Force | Where-Object {[IO.Path]::GetRelativePath($taskPublish,$_.FullName) -notmatch '^preview-data([\\/]|$)'})
    New-VerifiedZip $taskRuntimeZip $taskPublish $taskRuntimeFiles
    $taskSourceHash=(Get-FileHash -LiteralPath $taskSourceZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskRuntimeHash=(Get-FileHash -LiteralPath $taskRuntimeZip -Algorithm SHA256).Hash.ToLowerInvariant()
    Invoke-PreviewTool $CompilerPath @('/Qp',"/DPublishDir=$taskPublish",(Join-Path $taskRoot 'installer/GlassDesk.UiPreview.iss')) 'preview-installer-build.log'
    $taskInstaller=Join-Path $taskRelease "GlassDesk-$taskVersion-win-x64-ui-preview-setup.exe"
    if(!(Test-Path -LiteralPath $taskInstaller -PathType Leaf)){throw 'Preview installer output missing.'}
    $taskInstallerHash=(Get-FileHash -LiteralPath $taskInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskManifest=[ordered]@{
        Version=$taskVersion;Kind='UI Preview';BuildStartedUtc=$taskStarted;PackagedUtc=[DateTimeOffset]::UtcNow.ToString('O')
        ProductVersion=$taskProductVersion;Sdk=$taskSdk.Trim();NativeDirectory=[IO.Path]::GetRelativePath($taskRoot,$taskNative).Replace('\','/')
        SourceArchive=[IO.Path]::GetFileName($taskSourceZip);SourceSha256=$taskSourceHash
        RuntimeArchive=[IO.Path]::GetFileName($taskRuntimeZip);RuntimeSha256=$taskRuntimeHash
        InstallerFile=[IO.Path]::GetFileName($taskInstaller);InstallerSha256=$taskInstallerHash
        InstallerCompilerSha256=(Get-FileHash -LiteralPath $CompilerPath -Algorithm SHA256).Hash.ToLowerInvariant()
        SourceFiles=$taskSourceAfter;RuntimeFiles=(Get-FileManifest $taskPublish $taskRuntimeFiles)
        AutomatedChecks='native hidden ABI/message regression; Core; Platform; Plugin; Audio'
        NativeGuiAndResidency='Not determined by this build script; see the acceptance report.'
        AcceptanceReportIncluded=(Test-Path -LiteralPath (Join-Path $taskPublish 'AcceptanceReport.md'))
        FormalReleaseUnchanged=$true
    }
    # Confirm protected bytes before the manifest makes that factual statement.
    $taskFormalAfter=Get-FileManifest $taskFormal @(Get-ChildItem -LiteralPath $taskFormal -File -Recurse -Force)
    if(($taskFormalAfter | ConvertTo-Json -Compress) -ne $taskFormalJson){throw 'Protected formal 0.2.0 release changed.'}
    $taskManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRelease 'release-verification.json') -Encoding utf8
    @("$taskRuntimeHash  $([IO.Path]::GetFileName($taskRuntimeZip))","$taskSourceHash  $([IO.Path]::GetFileName($taskSourceZip))","$taskInstallerHash  $([IO.Path]::GetFileName($taskInstaller))") | Set-Content -LiteralPath (Join-Path $taskRelease 'SHA256SUMS.txt') -Encoding utf8
    Write-Output "UI Preview packaged: $taskRelease"
}finally{
    Pop-Location
    $taskFormalFinally=Get-FileManifest $taskFormal @(Get-ChildItem -LiteralPath $taskFormal -File -Recurse -Force)
    if(($taskFormalFinally | ConvertTo-Json -Compress) -ne $taskFormalJson){throw 'Protected formal 0.2.0 release changed during the preview build.'}
}
