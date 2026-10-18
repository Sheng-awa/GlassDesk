param([string]$CompilerPath='',
      [string]$FrozenBatchPath='verification/tested-binary-0.2.0.json',
      [switch]$SkipInstallerBuild)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskPublish=Join-Path $taskRoot 'artifacts/publish'
$taskInstallerDirectory=Join-Path $taskRoot 'artifacts/installer'
$taskReleaseDirectory=Join-Path $taskRoot 'artifacts/release/0.2.0'
if(!$CompilerPath){$CompilerPath=Join-Path $taskRoot '.tools/innosetup-7.1.0/ISCC.exe'}
if(![IO.Path]::IsPathRooted($FrozenBatchPath)){$FrozenBatchPath=Join-Path $taskRoot $FrozenBatchPath}
$taskFrozen=Get-Content -LiteralPath $FrozenBatchPath -Raw | ConvertFrom-Json
foreach($taskProperty in $taskFrozen.Files.PSObject.Properties){
    $taskActual=(Get-FileHash -LiteralPath (Join-Path $taskPublish $taskProperty.Name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if($taskActual -ne $taskProperty.Value){throw "Tested binary changed: $($taskProperty.Name)"}
}
# Documentation can be finalized after residency without rebuilding the tested app.
if($SkipInstallerBuild){
    $taskPrior=Get-Content -LiteralPath (Join-Path $taskRoot 'verification/release-binary-batch-0.2.0.json') -Raw | ConvertFrom-Json
    $taskExisting=Join-Path $taskInstallerDirectory 'GlassDesk-0.2.0-win-x64-setup.exe'
    if((Get-FileHash -LiteralPath $taskExisting -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskPrior.InstallerSha256){throw 'Existing installer no longer matches the recorded batch.'}
    if((Get-FileHash -LiteralPath (Join-Path $taskRoot 'docs/user-guide.md')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $taskPublish 'UserGuide.md')).Hash){throw 'User guide changed; rebuild the installer.'}
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/user-guide.md') -Destination (Join-Path $taskPublish 'UserGuide.md') -Force
if(!$SkipInstallerBuild){
    & $CompilerPath (Join-Path $taskRoot 'installer/GlassDesk.iss')
    if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
}
$taskInstaller=Join-Path $taskInstallerDirectory 'GlassDesk-0.2.0-win-x64-setup.exe'
$taskFiles=[ordered]@{}
foreach($taskFile in (Get-ChildItem -LiteralPath $taskPublish -File -Recurse | Sort-Object FullName)){
    $taskRelative=[IO.Path]::GetRelativePath($taskPublish,$taskFile.FullName).Replace('\','/')
    $taskFiles[$taskRelative]=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$taskManifest=[ordered]@{
    Version='0.2.0';At=[DateTimeOffset]::UtcNow.ToString('O')
    BuildLog='artifacts/final-acceptance/build-release-worker-final.log'
    AcceptanceReport='docs/test-report.md';TestedProcessId=$taskFrozen.Pid
    FrozenBinaryChecks='verification/tested-binary-0.2.0.json'
    InstallerSha256=(Get-FileHash -LiteralPath $taskInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    Files=$taskFiles
}
$taskManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskRoot 'verification/release-binary-batch-0.2.0.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'package-source.ps1')
$taskSource=Join-Path $taskInstallerDirectory 'GlassDesk-0.2.0-source.zip'
# Verify every archived byte against the authoritative checkout, not just selected files.
$taskArchive=[IO.Compression.ZipFile]::OpenRead($taskSource)
$taskChecked=0
$taskEntryNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
try {
    foreach($taskEntry in $taskArchive.Entries){
        if(!$taskEntry.Name){continue}
        $taskSourceFile=[IO.Path]::GetFullPath((Join-Path $taskRoot $taskEntry.FullName))
        if(!$taskSourceFile.StartsWith($taskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Source archive path escaped the checkout.'}
        $taskStream=$taskEntry.Open()
        try {$taskSha=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskStream)).ToLowerInvariant()} finally {$taskStream.Dispose()}
        if($taskSha -ne (Get-FileHash -LiteralPath $taskSourceFile -Algorithm SHA256).Hash.ToLowerInvariant()){throw "Stale source archive entry: $($taskEntry.FullName)"}
        if(!$taskEntryNames.Add($taskEntry.FullName)){throw 'Duplicate source archive entry.'}
        $taskChecked++
    }
} finally {$taskArchive.Dispose()}
foreach($taskFile in (Get-ChildItem -LiteralPath $taskRoot -File -Recurse -Force)){
    $taskRelative=[IO.Path]::GetRelativePath($taskRoot,$taskFile.FullName).Replace('\','/')
    if($taskRelative -match '(^|/)(artifacts|\.tools|bin|obj|\.git|\.vs|__pycache__)(/|$)'){continue}
    if(!$taskEntryNames.Contains($taskRelative)){throw "Missing source archive entry: $taskRelative"}
}
$taskSourceHash=(Get-FileHash -LiteralPath $taskSource -Algorithm SHA256).Hash.ToLowerInvariant()
@("$($taskManifest.InstallerSha256)  $([IO.Path]::GetFileName($taskInstaller))","$taskSourceHash  $([IO.Path]::GetFileName($taskSource))") | Set-Content -LiteralPath (Join-Path $taskInstallerDirectory 'SHA256SUMS.txt') -Encoding utf8
New-Item -ItemType Directory -Path $taskReleaseDirectory -Force | Out-Null
foreach($taskPath in @($taskInstaller,$taskSource,(Join-Path $taskInstallerDirectory 'SHA256SUMS.txt'))){Copy-Item -LiteralPath $taskPath -Destination $taskReleaseDirectory -Force}
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/test-report.md') -Destination (Join-Path $taskReleaseDirectory 'AcceptanceReport.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/test-report.md') -Destination (Join-Path $taskReleaseDirectory 'test-report.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/configuration-migration.md') -Destination (Join-Path $taskReleaseDirectory 'configuration-migration.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/user-guide.md') -Destination (Join-Path $taskReleaseDirectory 'UserGuide.md') -Force
@{Version='0.2.0';InstallerSha256=$taskManifest.InstallerSha256;SourceSha256=$taskSourceHash;ReportSha256=(Get-FileHash -LiteralPath (Join-Path $taskRoot 'docs/test-report.md') -Algorithm SHA256).Hash.ToLowerInvariant();SourceEntriesVerified=$taskChecked;BinaryFiles=$taskFiles.Count} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskReleaseDirectory 'release-verification.json') -Encoding utf8
Write-Output "Unified release verified: $taskReleaseDirectory ($taskChecked source files)."
