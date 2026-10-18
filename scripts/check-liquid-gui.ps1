param([Parameter(Mandatory)][int]$TargetProcessId,
      [Parameter(Mandatory)][long]$SettingsWindowId,
      [Parameter(Mandatory)][string]$OutputPath,
      [int]$Cycles=100)
$ErrorActionPreference='Stop'
$taskCua='C:/Users/Sheng/AppData/Local/Programs/Cua/cua-driver/bin/cua-driver.exe'
$taskTarget=@{pid=$TargetProcessId;window_id=$SettingsWindowId}
$taskResults=[Collections.Generic.List[object]]::new()
function Get-TestState {
    $taskJson=$taskTarget | ConvertTo-Json -Compress
    $taskState=& $taskCua get_window_state $taskJson | ConvertFrom-Json
    if($null -eq $taskState.elements){throw 'Settings snapshot unavailable.'}
    return $taskState
}
function Get-LiquidToggle($taskState) {
    $taskToggle=$taskState.elements | Where-Object {$_.role -eq 'Button' -and $_.label -eq '液态玻璃' -and $_.actions -contains 'toggle'} | Select-Object -First 1
    if($null -eq $taskToggle){throw 'The plugin page must remain selected.'}
    return $taskToggle
}
function Click-Liquid($taskState) {
    $taskToggle=Get-LiquidToggle $taskState
    $taskAction=@{pid=$TargetProcessId;window_id=$SettingsWindowId;element_token=$taskToggle.element_token} | ConvertTo-Json -Compress
    $taskResponse=& $taskCua click $taskAction | ConvertFrom-Json
    if($null -eq $taskResponse.route){throw 'Toggle action was rejected.'}
}
function Wait-Liquid([bool]$taskEnabled) {
    $taskDeadline=[Diagnostics.Stopwatch]::StartNew()
    do {
        $taskState=Get-TestState
        $taskToggle=Get-LiquidToggle $taskState
        $taskStatus=$taskState.elements | Where-Object { $_.role -eq 'Text' -and ($_.label.StartsWith('Running: WGC monitor') -or $_.label -eq 'Disabled') } | Select-Object -First 1
        if($taskToggle.selected -eq $taskEnabled -and $null -ne $taskStatus -and (($taskEnabled -and $taskStatus.label.StartsWith('Running:')) -or (!$taskEnabled -and $taskStatus.label -eq 'Disabled'))){return @{State=$taskState;Status=$taskStatus.label}}
    } while($taskDeadline.Elapsed.TotalSeconds -lt 8)
    throw 'Material did not reach its verified running/disabled state.'
}
$taskState=Get-TestState
if((Get-LiquidToggle $taskState).selected){throw 'Start with liquid glass disabled.'}
for($taskCycle=1;$taskCycle -le $Cycles;$taskCycle++) {
    Click-Liquid $taskState
    $taskOn=Wait-Liquid $true
    Click-Liquid $taskOn.State
    $taskOff=Wait-Liquid $false
    $taskState=$taskOff.State
    $taskProcess=Get-Process -Id $TargetProcessId
    $taskResults.Add(@{Cycle=$taskCycle;At=[DateTimeOffset]::UtcNow;Running=$taskOn.Status;Disabled=$taskOff.Status;PrivateBytes=$taskProcess.PrivateMemorySize64;Handles=$taskProcess.HandleCount})
    $taskProcess.Dispose()
    @{Test='Cua UIA production liquid glass active enable/disable';Pid=$TargetProcessId;RequestedCycles=$Cycles;CompletedCycles=$taskCycle;Cycles=$taskResults} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    if($taskCycle%10 -eq 0){Write-Output "Verified $taskCycle / $Cycles active WGC cycles."}
}
