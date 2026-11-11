[CmdletBinding()]
param([string]$Engine,[string]$Mock)
$ErrorActionPreference='Stop'
$run=Join-Path ([IO.Path]::GetTempPath()) ('ds5link-ipc-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $run | Out-Null
$database=Join-Path $run 'test.db'
Set-Content ($database+'.paused') 'paused'
Set-Content (Join-Path $run 'mock.txt') "--cloak-off`n--inv-off"
$name='DS5Link.Test.'+[Guid]::NewGuid().ToString('N')
$process=$null
$previousCli=$env:VDS_HIDHIDE_CLI
$previousState=$env:DS5LINK_MOCK_HIDHIDE_STATE
$previousAccessOutput=$env:DS5LINK_MOCK_HIDHIDE_ACCESS_OUTPUT
function Request([string]$line,[switch]$Fragmented) {
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$name,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect(3000)
        $bytes=[Text.Encoding]::UTF8.GetBytes($line+"`n")
        if($Fragmented) {
            for($i=0;$i -lt $bytes.Length;$i+=7) {$pipe.Write($bytes,$i,[Math]::Min(7,$bytes.Length-$i))}
        } else {$pipe.Write($bytes,0,$bytes.Length)}
        $pipe.Flush()
        $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8)
        $task=$reader.ReadLineAsync()
        if(!$task.Wait(4000)){throw 'Control reply timed out'}
        return ($task.Result | ConvertFrom-Json)
    } finally {$pipe.Dispose()}
}
try {
    $env:VDS_HIDHIDE_CLI=$Mock
    $env:DS5LINK_MOCK_HIDHIDE_STATE=Join-Path $run 'mock.txt'
    $process=Start-Process $Engine -ArgumentList @('--db-path',('"'+$database+'"'),'--log',('"'+(Join-Path $run 'log.txt')+'"'),'--pipe',('"\\.\pipe\'+$name+'"')) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $run 'stderr.txt')
    Start-Sleep -Milliseconds 1000
    $reply=Request '{"protocolVersion":1,"id":"one","command":"getState"}' -Fragmented
    if(!$reply.ok -or $reply.id -ne 'one' -or $reply.payload.state -ne 'Paused'){throw 'Invalid initial state'}
    if($reply.payload.autoDisconnectEnabled -ne $true){throw 'Auto-disconnect default is disabled'}
    $reply=Request '{"protocolVersion":1,"id":"idle-off","command":"setAutoDisconnect","options":{"enabled":false}}'
    if(!$reply.ok -or $reply.payload.autoDisconnectEnabled -ne $false){throw 'Auto-disconnect could not be disabled'}
    $reply=Request '{"protocolVersion":1,"id":"idle-invalid","command":"setAutoDisconnect","options":{"enabled":"true"}}'
    if($reply.ok){throw 'Auto-disconnect accepted an invalid setting'}
    $reply=Request '{"protocolVersion":1,"id":"policy-save","command":"setIdlePolicy","options":{"schemaVersion":1,"desktopMinutes":1,"gameMinutes":0,"keepConnectedWhileCharging":true}}'
    if(!$reply.ok -or $reply.payload.idlePolicy.desktopMinutes -ne 1 -or $reply.payload.autoDisconnectEnabled -ne $false -or $reply.payload.reasonCode -ne 'emulation_paused'){throw 'Idle policy save failed'}
    $reply=Request '{"protocolVersion":1,"id":"policy-invalid","command":"setIdlePolicy","options":{"schemaVersion":1,"desktopMinutes":5.0,"gameMinutes":0,"keepConnectedWhileCharging":true}}'
    if($reply.ok -or $reply.error -ne 'INVALID_IDLE_POLICY'){throw 'Invalid idle policy accepted'}
    $reply=Request '{"protocolVersion":2,"id":"two","command":"enable"}'
    if($reply.ok -or $reply.error -ne 'PROTOCOL_VERSION_MISMATCH'){throw 'Version mismatch accepted'}
    $reply=Request '{"protocolVersion":1,"id":"three","command":"pause"}'
    if(!$reply.ok -or $reply.payload.enabled){throw 'Pause failed'}
    $reply=Request '{"protocolVersion":1,"id":"four","command":"reconnect"}'
    if($reply.ok -or $reply.error -ne 'BRIDGE_PAUSED'){throw 'Reconnect bypassed pause'}
    $reply=Request '{"protocolVersion":1,"id":"five","command":"missing"}'
    if($reply.ok){throw 'Unknown command accepted'}
    $reply=Request '{bad json}'
    if($reply.OK -ne $false){throw 'Invalid JSON accepted'}
    $reply=Request '{"protocolVersion":1,"id":"disconnect-no-target","command":"disconnect"}'
    if($reply.ok -or $reply.error -ne 'CONTROLLER_NOT_CONFIGURED'){throw 'Disconnect accepted without an owned configured target'}
    $reply=Request '{"protocolVersion":1,"id":"after-disconnect-rejected","command":"getState"}'
    if(!$reply.ok -or $reply.payload.state -ne 'Paused' -or $reply.payload.bluetoothDisconnected){throw 'Rejected disconnect changed state'}
    if(Test-Path $database) {
        if((Get-Item $database).Length -gt 0){throw 'Read-only/paused tests captured a controller'}
    }
    $reply=Request '{"protocolVersion":1,"id":"input-preview","command":"getInputPreview"}'
    if(!$reply.ok -or $reply.payload.enabled -or $reply.payload.ageMs -ne -1 -or $reply.payload.touches.Count -ne 2){throw 'Read-only preview invented an active controller'}
    $reply=Request '{"protocolVersion":1,"id":"input-defaults","command":"getState"}'
    if($reply.payload.inputOptions.leftDeadzone -ne 0 -or $reply.payload.inputOptions.touchEnabled){throw 'Input defaults changed native behavior'}
    $reply=Request '{"protocolVersion":1,"id":"input-save","command":"setInputOptions","options":{"leftDeadzone":10,"rightDeadzone":15,"touchEnabled":false,"consumeTouchpad":true}}'
    if(!$reply.ok -or $reply.payload.inputOptions.leftDeadzone -ne 10 -or !(Test-Path ($database+'.input.json'))){throw 'Input settings were not applied and saved'}
    $reply=Request '{"protocolVersion":1,"id":"input-invalid","command":"setInputOptions","options":{"leftDeadzone":31,"rightDeadzone":0,"touchEnabled":false,"consumeTouchpad":true}}'
    if($reply.ok){throw 'Out-of-range input settings accepted'}
    $reply=Request '{"protocolVersion":1,"id":"input-events","command":"getInputEvents","afterSequence":0}'
    if(!$reply.ok -or $reply.payload.enabled -or !$reply.payload.epoch -or $reply.payload.events.Count -ne 0){throw 'Paused input event stream invalid'}
    $effectOptions=@{left=@{mode='game';strength=3;start=2;end=7;frequency=15};right=@{mode='game';strength=3;start=2;end=7;frequency=15};ledMode='game';red=10;green=132;blue=255;brightness=60;speed=50;audioEnabled=$false;bassOnly=$true;sensitivity=100;limit=35;gate=3}
    $reply=Request (@{protocolVersion=1;id='effect-save';command='setEffectOptions';options=$effectOptions}|ConvertTo-Json -Depth 5 -Compress)
    if(!$reply.ok -or $reply.payload.options.limit -ne 35 -or !(Test-Path ($database+'.effects.json'))){throw 'Effect settings were not saved'}
    $effectOptions.limit=81
    $reply=Request (@{protocolVersion=1;id='effect-invalid';command='setEffectOptions';options=$effectOptions}|ConvertTo-Json -Depth 5 -Compress)
    if($reply.ok){throw 'Unsafe audio effect limit accepted'}
    $reply=Request '{"protocolVersion":1,"id":"effect-level-invalid","command":"getEffects","audioLevel":81}'
    if($reply.ok){throw 'Out-of-range transient audio level accepted'}
    $reply=Request '{"protocolVersion":1,"id":"effect-state","command":"getEffects","audioLevel":0}'
    if(!$reply.ok -or !$reply.payload.leaseActive -or $reply.payload.options.limit -ne 35 -or $reply.payload.leftOwned){throw 'Effect lease or rejected-setting preservation failed'}
    foreach($samples in @(@(1,2), (@(128)*64))) {
        $reply=Request (@{protocolVersion=1;id='pcm-invalid';command='getEffects';audioFrames=@(@{samples=$samples;tick=1})}|ConvertTo-Json -Depth 6 -Compress)
        if($reply.ok){throw 'Malformed PCM waveform accepted'}
    }
    Set-Content ($database+'.hidhide-recovery.json') '{"schema":99}'
    $reply=Request '{"protocolVersion":1,"id":"pending-recovery","command":"getState"}'
    if(!$reply.ok -or $reply.payload.state -ne 'Error' -or !$reply.payload.recoveryPending){throw 'Pending recovery was reported as released'}
    $reply=Request '{"protocolVersion":1,"id":"failed-recovery","command":"recover"}'
    if($reply.ok -or !(Test-Path ($database+'.hidhide-recovery.json'))){throw 'Failed recovery lost its journal'}
    Remove-Item -LiteralPath ($database+'.hidhide-recovery.json')
    $reply=Request '{"protocolVersion":1,"id":"clear-recovery","command":"recover"}'
    if(!$reply.ok -or $reply.payload.state -ne 'Paused'){throw 'Completed recovery did not release controller'}
    Set-Content -LiteralPath $env:DS5LINK_MOCK_HIDHIDE_STATE -Value @('--cloak-on','--inv-on','--dev-hide "HID\OTHER"','--app-reg "C:\OtherApp.exe"')
    $reply=Request '{"protocolVersion":1,"id":"normalize-hidhide","command":"normalizeHidHide"}'
    if(!$reply.ok -or $reply.payload.enabled -or $reply.payload.state -ne 'Paused'){throw 'HidHide repair did not preserve pause'}
    $mockState=Get-Content $env:DS5LINK_MOCK_HIDHIDE_STATE -Raw
    if($mockState -notmatch '--inv-off' -or $mockState -notmatch '--cloak-on' -or $mockState -notmatch 'HID\\OTHER' -or $mockState -notmatch 'OtherApp.exe'){throw 'HidHide repair changed unrelated configuration'}
    $reply=Request '{"protocolVersion":1,"id":"normalize-hidhide-again","command":"normalizeHidHide"}'
    if(!$reply.ok){throw 'HidHide repair was not idempotent'}
    # Simulate startup recovery failing because the HidHide GUI owns the handle.
    Stop-Process -Id $process.Id;$process.WaitForExit()
    @{schema=1;device='HID\TARGET';daemon=[IO.Path]::GetFullPath($Engine);hideAdded=$true;cloakEnabled=$false;appAdded=$false}|ConvertTo-Json|Set-Content ($database+'.hidhide-recovery.json') -Encoding utf8
    $env:DS5LINK_MOCK_HIDHIDE_ACCESS_OUTPUT='1'
    $process=Start-Process $Engine -ArgumentList @('--db-path',('"'+$database+'"'),'--log',('"'+(Join-Path $run 'restart-log.txt')+'"'),'--pipe',('"\\.\pipe\'+$name+'"')) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $run 'restart-stderr.txt')
    $env:DS5LINK_MOCK_HIDHIDE_ACCESS_OUTPUT=$previousAccessOutput
    Start-Sleep -Milliseconds 1000
    $reply=Request '{"protocolVersion":1,"id":"policy-after-restart","command":"getState"}'
    if(!$reply.ok -or $reply.payload.idlePolicy.desktopMinutes -ne 1 -or $reply.payload.idlePolicy.gameMinutes -ne 0 -or !$reply.payload.idlePolicy.keepConnectedWhileCharging -or $reply.payload.autoDisconnectEnabled){throw 'Idle policy or legacy switch lost after restart'}
    $reply=Request '{"protocolVersion":1,"id":"startup-recovery-failure","command":"getState"}'
    if(!$reply.ok -or !$reply.payload.recoveryPending -or $reply.payload.state -ne 'Error'){throw 'Startup recovery failure prevented diagnostic control access'}
    if($reply.payload.autoDisconnectEnabled -ne $false){throw 'Auto-disconnect setting lost on restart'}
    if(!(Test-Path ($database+'.hidhide-recovery.json'))){throw 'Startup recovery failure lost its journal'}
    if((Get-Content (Join-Path $run 'restart-log.txt') -Raw) -notmatch 'HIDHIDE_ACCESS_DENIED'){throw 'Startup failure did not retain the exact HidHide access error'}
    Remove-Item -LiteralPath ($database+'.hidhide-recovery.json')
    $reply=Request '{"protocolVersion":1,"id":"startup-recovery-cleared","command":"recover"}'
    if(!$reply.ok -or $reply.payload.state -ne 'Paused'){throw 'Diagnostic service could not clear recovered state'}
    $reply=Request '{"protocolVersion":1,"id":"idle-on","command":"setAutoDisconnect","options":{"enabled":true}}'
    if(!$reply.ok -or $reply.payload.autoDisconnectEnabled -ne $true -or (Test-Path -LiteralPath ($database+'.auto-disconnect-disabled'))){throw 'Auto-disconnect could not be re-enabled persistently'}
    Write-Host 'Control protocol tests passed (mock HidHide, no hardware capture)'
} finally {
    if($process -and !$process.HasExited){Stop-Process -Id $process.Id;$process.WaitForExit()}
    $env:VDS_HIDHIDE_CLI=$previousCli
    $env:DS5LINK_MOCK_HIDHIDE_STATE=$previousState
    $env:DS5LINK_MOCK_HIDHIDE_ACCESS_OUTPUT=$previousAccessOutput
}
