param(
    [string]$Project='samples\StatusPlugin\StatusPlugin.csproj',
    [string]$Manifest='samples\StatusPlugin\plugin.json',
    [string]$OutputDirectory='artifacts\plugins'
)
$ErrorActionPreference='Stop'
$pluginTaskRoot=Split-Path -Parent $PSScriptRoot
$pluginTaskProject=[IO.Path]::GetFullPath((Join-Path $pluginTaskRoot $Project))
$pluginTaskManifest=[IO.Path]::GetFullPath((Join-Path $pluginTaskRoot $Manifest))
$pluginTaskOutput=[IO.Path]::GetFullPath((Join-Path $pluginTaskRoot $OutputDirectory))
if(-not $pluginTaskOutput.StartsWith($pluginTaskRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Plugin output must be inside GlassDesk.'}
$pluginTaskMetadata=Get-Content -LiteralPath $pluginTaskManifest -Raw | ConvertFrom-Json
if($pluginTaskMetadata.id -notmatch '^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$' -or $pluginTaskMetadata.version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$'){throw 'Invalid plugin id/version.'}
$pluginTaskStage=Join-Path $pluginTaskOutput ('staging-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $pluginTaskStage -Force | Out-Null
try {
    & dotnet restore $pluginTaskProject --locked-mode
    if($LASTEXITCODE -ne 0){throw 'Locked plugin restore failed.'}
    & dotnet publish $pluginTaskProject -c Release --no-restore --self-contained false -o $pluginTaskStage -m:1 -p:BuildInParallel=false
    if($LASTEXITCODE -ne 0){throw 'Plugin publish failed.'}
    Copy-Item -LiteralPath $pluginTaskManifest -Destination (Join-Path $pluginTaskStage 'plugin.json')
    if(-not (Test-Path -LiteralPath (Join-Path $pluginTaskStage $pluginTaskMetadata.entryAssembly))){throw 'Plugin entry assembly is missing.'}
    $pluginTaskPackage=Join-Path $pluginTaskOutput ($pluginTaskMetadata.id+'-'+$pluginTaskMetadata.version+'.glassdesk-plugin')
    $pluginTaskStream=[IO.File]::Open($pluginTaskPackage,[IO.FileMode]::Create,[IO.FileAccess]::Write)
    $pluginTaskArchive=[IO.Compression.ZipArchive]::new($pluginTaskStream,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach($pluginTaskFile in (Get-ChildItem -LiteralPath $pluginTaskStage -File -Recurse | Sort-Object FullName)) {
            $pluginTaskName=[IO.Path]::GetRelativePath($pluginTaskStage,$pluginTaskFile.FullName).Replace('\','/')
            if($pluginTaskName -eq 'GlassDesk.Core.dll' -or $pluginTaskName.EndsWith('.pdb')){continue}
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($pluginTaskArchive,$pluginTaskFile.FullName,$pluginTaskName,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {$pluginTaskArchive.Dispose();$pluginTaskStream.Dispose()}
    $pluginTaskHash=(Get-FileHash -LiteralPath $pluginTaskPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    "$pluginTaskHash  $([IO.Path]::GetFileName($pluginTaskPackage))" | Set-Content -LiteralPath ($pluginTaskPackage+'.sha256') -Encoding utf8
    Write-Output $pluginTaskPackage
} finally {
    if(Test-Path -LiteralPath $pluginTaskStage){
        $pluginTaskResolved=(Resolve-Path -LiteralPath $pluginTaskStage).Path
        if(-not $pluginTaskResolved.StartsWith($pluginTaskOutput+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Staging cleanup is outside plugin output.'}
        Remove-Item -LiteralPath $pluginTaskResolved -Recurse -Force
    }
}
