param([Parameter(Mandatory)][int]$TargetProcessId,
      [Parameter(Mandatory)][string]$OutputDirectory,
      [int]$MaximumSeconds=4800)
$ErrorActionPreference='Stop'
# Explicit QA observer: counters only, no input, audio, screenshots or settings changes.
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$taskSamples=Join-Path $OutputDirectory 'external-resources.jsonl'
$taskPhase=Join-Path $OutputDirectory 'phase.txt'
$taskStop=Join-Path $OutputDirectory 'stop-observer'
$taskClock=[Diagnostics.Stopwatch]::StartNew()
while($taskClock.Elapsed.TotalSeconds -lt $MaximumSeconds -and -not (Test-Path -LiteralPath $taskStop)) {
    $taskProcess=Get-Process -Id $TargetProcessId -ErrorAction SilentlyContinue
    if($null -eq $taskProcess){break}
    $taskRecord=[ordered]@{At=[DateTimeOffset]::UtcNow.ToString('O');ElapsedSeconds=$taskClock.Elapsed.TotalSeconds;Pid=$TargetProcessId;Phase='unspecified'}
    if(Test-Path -LiteralPath $taskPhase){$taskRecord.Phase=[IO.File]::ReadAllText($taskPhase).Trim()}
    try {$taskRecord.PrivateBytes=$taskProcess.PrivateMemorySize64;$taskRecord.Handles=$taskProcess.HandleCount;$taskRecord.CpuSeconds=$taskProcess.TotalProcessorTime.TotalSeconds} catch {$taskRecord.ProcessCounterError=$_.Exception.GetType().Name}
    try {
        $taskPrefix="pid_${TargetProcessId}_"
        $taskRecord.GpuMemory=@(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory -Filter "Name LIKE '${taskPrefix}%'" | Where-Object {$_.Name.StartsWith($taskPrefix)} | Select-Object Name,DedicatedUsage,SharedUsage)
        $taskRecord.GpuEngines=@(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -Filter "Name LIKE '${taskPrefix}%'" | Where-Object {$_.Name.StartsWith($taskPrefix)} | Select-Object Name,UtilizationPercentage)
        $taskRecord.CaptureWorkers=@(Get-CimInstance Win32_Process -Filter "ParentProcessId=$TargetProcessId AND Name='GlassDesk.CaptureWorker.exe'" | ForEach-Object {
            $taskChild=$_;$taskChildPrefix="pid_$($taskChild.ProcessId)_"
            @{Pid=$taskChild.ProcessId;PrivateBytes=$taskChild.PrivatePageCount;Handles=$taskChild.HandleCount;Cpu100ns=([double]$taskChild.KernelModeTime+[double]$taskChild.UserModeTime);Gpu=@(Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory -Filter "Name LIKE '${taskChildPrefix}%'" | Where-Object {$_.Name.StartsWith($taskChildPrefix)} | Select-Object Name,DedicatedUsage,SharedUsage)}
        })
        $taskRecord.Compositor=@(Get-CimInstance Win32_Process -Filter "Name='dwm.exe'" | Where-Object {$_.SessionId -eq $taskProcess.SessionId} | Select-Object ProcessId,KernelModeTime,UserModeTime,PrivatePageCount)
    } catch {$taskRecord.GpuCounterError=$_.Exception.GetType().Name}
    $taskRecord | ConvertTo-Json -Depth 5 -Compress | Add-Content -LiteralPath $taskSamples -Encoding utf8
    $taskProcess.Dispose()
    Start-Sleep -Seconds 15
}
Write-Output "Resource observer stopped after $([Math]::Round($taskClock.Elapsed.TotalSeconds,1)) seconds."
