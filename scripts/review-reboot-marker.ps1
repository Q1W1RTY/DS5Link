$ErrorActionPreference='Stop'
# Evaluate the installer decision with a mocked boot time and marker.
# No installer execution, registry writes or real service operations.
$lines=@(Get-Content (Join-Path $PSScriptRoot '../installer/Install.ps1') |
 Where-Object { $_ -match '^\$(pendingReboot|requiresReboot)=' })
if($lines.Count -ne 2){throw 'Installer decision changed; update the test.'}
$boot=[datetime]::UtcNow
function Check-RebootDecision($marker,$RebootRequired,$UpdateOnly,$expected){
 $os=[pscustomobject]@{LastBootUpTime=$boot}
 Invoke-Expression ($lines -join "`n")
 if($requiresReboot -ne $expected){throw 'Incorrect reboot decision.'}
}
Check-RebootDecision @{LastWriteTimeUtc=$boot.AddHours(-1)} $false $true $false
Check-RebootDecision @{LastWriteTimeUtc=$boot.AddMinutes(1)} $false $true $true
Check-RebootDecision $null $false $true $false
Check-RebootDecision $null $true $true $true
Check-RebootDecision @{LastWriteTimeUtc=$boot.AddMinutes(1)} $false $false $false
Write-Output 'Five reboot-marker scenarios passed.'
