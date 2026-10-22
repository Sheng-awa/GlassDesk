param([string]$CompilerPath,[switch]$SkipStress)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskVersion=([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
Push-Location -LiteralPath $taskRoot
try {
    $taskPublishPath=Join-Path $taskRoot 'artifacts\publish'
    if(Test-Path -LiteralPath $taskPublishPath) {
        $taskResolvedPublish=(Resolve-Path -LiteralPath $taskPublishPath).Path
        if($taskResolvedPublish -ne [IO.Path]::GetFullPath($taskPublishPath) -or -not $taskResolvedPublish.StartsWith($taskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Publish output is outside GlassDesk.'}
        Remove-Item -LiteralPath $taskResolvedPublish -Recurse -Force
    }
    if ($SkipStress) {
        & (Join-Path $PSScriptRoot 'build-native.ps1')
    } else {
        & (Join-Path $PSScriptRoot 'build-native.ps1') -BuildRegression
    }
    if ($LASTEXITCODE -ne 0) {throw 'Native build failed.'}
    if (-not $SkipStress) {
        & (Join-Path $taskRoot 'artifacts\native\Release\GlassDesk.NativeRegression.exe') | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts\native\Release\regression-result.json') -Encoding utf8
        if ($LASTEXITCODE -ne 0) {throw 'Hidden native regression failed.'}
    }
    & dotnet restore '.\src\GlassDesk.App\GlassDesk.App.csproj' --locked-mode
    if ($LASTEXITCODE -ne 0) {throw 'Locked dependency restore failed.'}
    foreach ($taskTestProject in @('.\tests\GlassDesk.Core.Tests\GlassDesk.Core.Tests.csproj','.\tests\GlassDesk.PlatformChecks\GlassDesk.PlatformChecks.csproj','.\tests\GlassDesk.PluginChecks\GlassDesk.PluginChecks.csproj','.\tests\GlassDesk.AudioChecks\GlassDesk.AudioChecks.csproj')) {
        & dotnet restore $taskTestProject --locked-mode
        if ($LASTEXITCODE -ne 0) {throw "Locked test restore failed: $taskTestProject"}
        & dotnet build $taskTestProject -c Release --no-restore -m:1 -p:BuildInParallel=false
        if ($LASTEXITCODE -ne 0) {throw "Check build failed: $taskTestProject"}
        $taskRunArguments=@('run','--project',$taskTestProject,'-c','Release','--no-build','--no-restore')
        if ($SkipStress -and $taskTestProject -eq '.\tests\GlassDesk.Core.Tests\GlassDesk.Core.Tests.csproj') {
            $taskRunArguments+=@('--','--no-stress')
        }
        & dotnet @taskRunArguments
        if ($LASTEXITCODE -ne 0) {throw "Checks failed: $taskTestProject"}
    }
    & dotnet publish '.\src\GlassDesk.App\GlassDesk.App.csproj' -c Release -r win-x64 --self-contained true --no-restore -o '.\artifacts\publish' -p:PublishSingleFile=false -p:PublishTrimmed=false -m:1 -p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) {throw 'Self-contained publish failed.'}
    Copy-Item -LiteralPath '.\docs\user-guide.md' -Destination '.\artifacts\publish\UserGuide.md'
    $taskPublicDocs=Join-Path $taskPublishPath 'docs'
    New-Item -ItemType Directory -Path $taskPublicDocs -Force | Out-Null
    foreach($taskDocument in @('index.html','showcase-maintenance.md','user-guide.md','privacy.md','known-issues.md')) {
        Copy-Item -LiteralPath (Join-Path $taskRoot "docs/$taskDocument") -Destination $taskPublicDocs -Force
    }
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/images') -Destination $taskPublicDocs -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination $taskPublishPath -Force
    $taskRootGuide=Get-Content -LiteralPath (Join-Path $taskPublishPath 'UserGuide.md') -Raw
    $taskRootGuide.Replace('(privacy.md)','(docs/privacy.md)').Replace('(known-issues.md)','(docs/known-issues.md)') | Set-Content -LiteralPath (Join-Path $taskPublishPath 'UserGuide.md') -Encoding utf8
    Copy-Item -LiteralPath '.\THIRD-PARTY-NOTICES.md' -Destination '.\artifacts\publish\THIRD-PARTY-NOTICES.md'
    Copy-Item -LiteralPath '.\LICENSE' -Destination '.\artifacts\publish\LICENSE'
    $taskLicenseDestination=Join-Path $taskRoot 'artifacts\publish\licenses'
    if (Test-Path -LiteralPath $taskLicenseDestination) {
        $taskResolvedLicense=(Resolve-Path -LiteralPath $taskLicenseDestination).Path
        if ($taskResolvedLicense -ne [IO.Path]::GetFullPath($taskLicenseDestination) -or -not $taskResolvedLicense.StartsWith($taskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'License output is outside the project.'}
        Remove-Item -LiteralPath $taskResolvedLicense -Recurse -Force
    }
    Copy-Item -LiteralPath '.\licenses' -Destination $taskLicenseDestination -Recurse -Force
    if (-not $CompilerPath) {$CompilerPath=Join-Path $taskRoot '.tools\innosetup-7.1.0\ISCC.exe'}
    if (-not (Test-Path -LiteralPath $CompilerPath)) {throw 'Inno Setup 7.1.0 x64 ISCC.exe is required. Pass -CompilerPath or provision the documented portable compiler.'}
    & $CompilerPath --quiet "/DAppVersion=$taskVersion" '.\installer\GlassDesk.iss'
    if ($LASTEXITCODE -ne 0) {throw 'Installer build failed.'}
    $taskInstaller=Join-Path $taskRoot "artifacts\installer\GlassDesk-$taskVersion-win-x64-setup.exe"
    $taskHash=(Get-FileHash -LiteralPath $taskInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    "$taskHash  GlassDesk-$taskVersion-win-x64-setup.exe" | Set-Content -LiteralPath (Join-Path (Split-Path -Parent $taskInstaller) 'SHA256SUMS.txt') -Encoding utf8
    Write-Output $taskInstaller
} finally {Pop-Location}

