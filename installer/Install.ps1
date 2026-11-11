[CmdletBinding()]
param([string]$OwnerSid,[switch]$InstallerMode,[string]$InstallDir,[switch]$ValidateOnly,[switch]$RebootRequired,[switch]$UpdateOnly)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
. (Join-Path $PSScriptRoot 'InstallPaths.ps1')
$install=Resolve-DS5InstallPath $InstallDir
if($ValidateOnly){Assert-DS5InstallTarget $install;Write-Output $install;exit 0}
if(!$OwnerSid){$OwnerSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value}
$null=[Security.Principal.SecurityIdentifier]::new($OwnerSid)
$admin=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if(!$admin){
 $arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$PSCommandPath+'"'),'-OwnerSid',$OwnerSid)
 $arguments+=@('-InstallDir',('"'+$install+'"'))
 if($InstallerMode){$arguments+='-InstallerMode'}
 if($RebootRequired){$arguments+='-RebootRequired'}
 if($UpdateOnly){$arguments+='-UpdateOnly'}
 $child=Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -Wait -PassThru
 if($child.ExitCode -notin @(0,3010)){throw "Installation failed: $($child.ExitCode). See %ProgramData%\DS5Link\setup.log"}
 if($InstallerMode){exit $child.ExitCode}
 Write-Host 'DS5Link installed. Open DS5Link from the Start menu.';exit 0
}
Assert-DS5InstallTarget $install
$transactionLock=[Threading.Mutex]::new($false,'Global\DS5Link.Install.Transaction')
try{$ownsTransaction=$transactionLock.WaitOne(0)}catch [Threading.AbandonedMutexException]{$ownsTransaction=$true}
if(!$ownsTransaction){throw 'Another DS5Link installation or removal is running.'}
$data=Join-Path $env:ProgramData 'DS5Link'
$serviceName='DS5LinkBridge'
if([int](Get-CimInstance Win32_OperatingSystem).BuildNumber -lt 22000){throw 'DS5Link v1 requires Windows 11.'}
if($env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -and $env:PROCESSOR_ARCHITEW6432 -ne 'AMD64'){throw 'This package supports Windows x64 only.'}
$registry='HKLM:\SOFTWARE\DS5Link'
if(Test-Path $registry){$previousOwner=(Get-ItemProperty $registry -Name OwnerSid -ErrorAction SilentlyContinue).OwnerSid;if($previousOwner -and $previousOwner -ne $OwnerSid){throw 'DS5Link belongs to another user. Ownership transfer is not automatic.'}}
$existing=Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
if($existing -and !$existing.PathName.StartsWith(('"'+$install+'\engine\vdsd.exe"'),[StringComparison]::OrdinalIgnoreCase)){throw 'An unrelated service uses the DS5Link service name.'}
if($UpdateOnly -and (!$existing -or !(Get-DS5RegisteredInstall) -or
 !(Test-Path -LiteralPath (Join-Path $install 'DS5Link.exe') -PathType Leaf))){
 throw 'Update requires an existing DS5Link installation.'
}
function Set-ProtectedDirectory([string]$Path,[switch]$OwnerRead) {
 if((Test-Path $Path) -and ((Get-Item -LiteralPath $Path).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'A protected application directory is a reparse point.'}
 New-Item -ItemType Directory $Path -Force | Out-Null
 $acl=[Security.AccessControl.DirectorySecurity]::new();$acl.SetAccessRuleProtection($true,$false)
 $administrators=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544');$acl.SetOwner($administrators)
 foreach($sid in @('S-1-5-18','S-1-5-32-544')){$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),'FullControl','ContainerInherit,ObjectInherit','None','Allow'))}
 $reader=if($OwnerRead){$OwnerSid}else{'S-1-5-32-545'}
 $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($reader),'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow'))
 Set-Acl -LiteralPath $Path -AclObject $acl
}
Set-ProtectedDirectory $data -OwnerRead
if($InstallerMode){
 Start-Transcript -Path (Join-Path $data 'setup.log') -Force | Out-Null
 trap {Write-Output $_.Exception.ToString();Stop-Transcript -ErrorAction SilentlyContinue | Out-Null;exit 1}
}
function Verify-Package([string]$Path,[string]$Expected) {
 if(!(Test-Path -LiteralPath $Path) -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected){throw "Package hash mismatch: $Path"}
 if((Get-AuthenticodeSignature -LiteralPath $Path).Status -ne 'Valid'){throw "Package signature invalid: $Path"}
}
$requiresReboot=[bool]$RebootRequired -or ($UpdateOnly -and (Test-Path (Join-Path $data 'reboot-required.txt')))
$dependencies=Get-Content (Join-Path $PSScriptRoot 'dependencies.json') -Raw | ConvertFrom-Json
. (Join-Path $PSScriptRoot 'Dependencies.ps1')
foreach($dependency in $dependencies){
 if($UpdateOnly){break}
 $code=Install-DS5Dependency $dependency
 if($code -eq 3010){$requiresReboot=$true;Set-Content (Join-Path $data 'reboot-required.txt') 'Restart Windows before hardware acceptance.'}
}
if($requiresReboot){Set-Content (Join-Path $data 'reboot-required.txt') 'Restart Windows before hardware acceptance.'}
elseif(Test-Path (Join-Path $data 'reboot-required.txt')){Remove-Item -LiteralPath (Join-Path $data 'reboot-required.txt')}
$registrationBefore=@{}
$shortcutBefore=@{}
foreach($name in @('DS5Link.lnk')){
 $path=Join-Path ([Environment]::GetFolderPath('CommonPrograms')) $name
 $shortcutBefore[$path]=if(Test-Path -LiteralPath $path){[IO.File]::ReadAllBytes($path)}else{$null}
}
$runBefore='Registry::HKEY_USERS\'+$OwnerSid+'\Software\Microsoft\Windows\CurrentVersion\Run'
$startupBefore=(Get-ItemProperty $runBefore -Name DS5Link -ErrorAction SilentlyContinue).DS5Link
foreach($key in @('HKLM:\SOFTWARE\DS5Link','HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DS5Link')){
 $values=@{}
 if(Test-Path $key){$native=Get-Item $key;foreach($name in $native.GetValueNames()){$values[$name]=@{Value=$native.GetValue($name);Kind=$native.GetValueKind($name)}}}
 $registrationBefore[$key]=$values
}
$stamp=[Guid]::NewGuid().ToString('N')
$installParent=[IO.Path]::GetDirectoryName($install)
$installName=[IO.Path]::GetFileName($install)
# Create only missing parents, with the same protected ACL as the installation.
$missingParents=@();$cursor=$installParent
while(!(Test-Path -LiteralPath $cursor)){$missingParents+=,$cursor;$cursor=[IO.Path]::GetDirectoryName($cursor)}
for($i=$missingParents.Count-1;$i -ge 0;$i--){Set-ProtectedDirectory $missingParents[$i]}
$stage=Join-Path $installParent ($installName+'.stage-'+$stamp)
$backup=Join-Path $installParent ($installName+'.backup-'+$stamp)
foreach($path in @($install,$stage,$backup)){
 if([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)) -ne $installParent){throw 'Unexpected application destination'}
 if((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Application destination is a reparse point'}
}
function Move-OwnedDirectory([string]$Source,[string]$Destination) {
 foreach($path in @($Source,$Destination)){
  if([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)) -ne $installParent){throw 'Unexpected move destination'}
  if($path -ne $install -and [IO.Path]::GetFileName($path) -notmatch ('^'+[regex]::Escape($installName)+'\.(stage|backup|failed)-[a-f0-9]{32}$')){throw 'Unexpected owned move path'}
  if((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Move path is a reparse point'}
 }
 $deadline=[DateTime]::UtcNow.AddSeconds(10)
 do {
  try{Move-Item -LiteralPath $Source -Destination $Destination -ErrorAction Stop;return}
  catch [IO.IOException]{if([DateTime]::UtcNow -ge $deadline){throw};Start-Sleep -Milliseconds 200}
 }while($true)
}
Set-ProtectedDirectory $stage
Copy-Item (Join-Path $PSScriptRoot 'app\*') $stage -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'Uninstall.ps1') $stage
Copy-Item (Join-Path $PSScriptRoot 'InstallPaths.ps1') $stage
Copy-Item (Join-Path $PSScriptRoot 'OwnedFiles.ps1') $stage
. (Join-Path $PSScriptRoot 'OwnedFiles.ps1')
New-AppOwnedManifest $stage
$destinationExisted=Test-Path -LiteralPath $install
$wasInstalled=[bool](Get-DS5RegisteredInstall)
$swapped=$false
try{
 if($existing){
  $serviceProcess=if($existing.ProcessId){Get-Process -Id $existing.ProcessId -ErrorAction SilentlyContinue}else{$null}
  try{
   Stop-Service $serviceName; (Get-Service $serviceName).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
   if($serviceProcess -and !$serviceProcess.WaitForExit(15000)){throw 'Service process did not release its files'}
  }finally{if($serviceProcess){$serviceProcess.Dispose()}}
 }
 foreach($uiProcess in @(Get-Process DS5Link,DS5Link -ErrorAction SilentlyContinue | Where-Object {$_.Path -and $_.Path.StartsWith($install+'\',[StringComparison]::OrdinalIgnoreCase)})){
  try{Stop-Process -InputObject $uiProcess -Force;if(!$uiProcess.WaitForExit(15000)){throw 'UI process did not release its files'}}finally{$uiProcess.Dispose()}
 }
 if($destinationExisted){Move-OwnedDirectory $install $backup}
 Move-OwnedDirectory $stage $install;$swapped=$true
 Set-ProtectedDirectory $install
 if(!$wasInstalled){Set-Content (Join-Path $data 'bridge.db.paused') 'paused'}
 $binary='"'+(Join-Path $install 'engine\vdsd.exe')+'" --db-path "'+(Join-Path $data 'bridge.db')+'" --log "'+(Join-Path $data 'bridge.log')+'" --pipe "\\.\pipe\DS5Link.Control.v1" --owner-sid "'+$OwnerSid+'"'
 # Use structured CIM arguments: Windows PowerShell 5 loses embedded quotes
 # when a quoted executable path is forwarded through sc.exe arguments.
 if($existing){
  $registered=Invoke-CimMethod -InputObject $existing -MethodName Change -Arguments @{PathName=$binary;StartMode='Automatic';DisplayName='DS5Link'}
 }else{
  $registered=Invoke-CimMethod -ClassName Win32_Service -MethodName Create -Arguments @{Name=$serviceName;DisplayName='DS5Link';PathName=$binary;ServiceType=[byte]16;ErrorControl=[byte]1;StartMode='Automatic';DesktopInteract=$false;StartName='LocalSystem'}
 }
 if($registered.ReturnValue -ne 0){throw "Cannot register the DS5Link service: $($registered.ReturnValue)"}
 & sc.exe description $serviceName 'Local DualSense Bluetooth to virtual USB bridge' | Out-Null
 & sc.exe failure $serviceName reset= 86400 actions= 'restart/5000/restart/15000/restart/60000' | Out-Null
 $ownerKey=[Microsoft.Win32.Registry]::LocalMachine.CreateSubKey('SOFTWARE\DS5Link',$true)
 try{$ownerKey.SetValue('OwnerSid',$OwnerSid,[Microsoft.Win32.RegistryValueKind]::String);$ownerKey.SetValue('InstallLocation',$install,[Microsoft.Win32.RegistryValueKind]::String)}finally{$ownerKey.Close()}
 if(!$requiresReboot){
 Start-Service $serviceName
 (Get-Service $serviceName).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
 # A running SCM process alone does not prove that its control endpoint is usable.
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.','DS5Link.Control.v1',[IO.Pipes.PipeDirection]::InOut)
 try{
  $pipe.Connect(15000)
  $writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),1024,$true);$writer.AutoFlush=$true
  $reader=[IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8,$false,1024,$true)
  $id=[guid]::NewGuid().ToString('N')
  $writer.WriteLine((@{protocolVersion=1;id=$id;command='hello'}|ConvertTo-Json -Compress))
  $replyTask=$reader.ReadLineAsync()
  if(!$replyTask.Wait(5000)){throw 'DS5Link control endpoint timed out'}
  $reply=$replyTask.Result|ConvertFrom-Json
  if(!$reply.ok -or $reply.id -ne $id -or $reply.protocolVersion -ne 1){throw 'DS5Link control endpoint returned an invalid response'}
 }finally{$pipe.Dispose()}
 }
 $uninstallKey='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DS5Link'
 $uninstallNative=[Microsoft.Win32.Registry]::LocalMachine.CreateSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\DS5Link',$true)
 try{
  $uninstallNative.SetValue('DisplayName','DS5Link');$uninstallNative.SetValue('DisplayVersion',([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $install 'DS5Link.exe')).ProductVersion -split '\+')[0]);$uninstallNative.SetValue('InstallLocation',$install)
  $uninstallNative.SetValue('DisplayIcon',(Join-Path $install 'DS5Link.exe')+',0')
  $uninstallNative.SetValue('UninstallString',('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $install 'Uninstall.ps1')+'"'))
 }finally{$uninstallNative.Close()}
 $shell=New-Object -ComObject WScript.Shell
 $shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'DS5Link.lnk'))
 $shortcut.TargetPath=Join-Path $install 'DS5Link.exe';$shortcut.IconLocation=(Join-Path $install 'DS5Link.exe')+',0';$shortcut.WorkingDirectory=$install;$shortcut.Save()
 $run='Registry::HKEY_USERS\'+$OwnerSid+'\Software\Microsoft\Windows\CurrentVersion\Run'
 if(Test-Path $run){
  $startup=(Get-ItemProperty $run -Name DS5Link -ErrorAction SilentlyContinue).DS5Link
  if($startup -and $startup.StartsWith(('"'+(Join-Path $install 'DS5Link.exe')+'"'),[StringComparison]::OrdinalIgnoreCase)){
   Set-ItemProperty $run -Name DS5Link -Value ('"'+(Join-Path $install 'DS5Link.exe')+'" --background')
  }
 }
 if($wasInstalled){Write-Host "Previous application files retained for rollback: $backup"}
 Write-Host 'DS5Link installed. The bridge starts paused until enabled from the app.'
}catch{
 $failure=$_
 Stop-Service $serviceName -ErrorAction SilentlyContinue
 $stopped=Get-Service $serviceName -ErrorAction SilentlyContinue
 if($stopped){$stopped.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))}
 if($swapped -and (Test-Path -LiteralPath $install)){
  $failed=Join-Path $installParent ($installName+'.failed-'+$stamp);Move-OwnedDirectory $install $failed
 }
 if(Test-Path -LiteralPath $backup){Move-OwnedDirectory $backup $install}
 if(!$existing){& sc.exe delete $serviceName | Out-Null;Remove-Item $registry -Recurse -ErrorAction SilentlyContinue}
 else {Invoke-CimMethod -InputObject $existing -MethodName Change -Arguments @{PathName=$existing.PathName;StartMode=$(if($existing.StartMode -eq 'Auto'){'Automatic'}else{$existing.StartMode});DisplayName=$existing.DisplayName} | Out-Null;if($existing.State -eq 'Running'){Start-Service $serviceName -ErrorAction SilentlyContinue}}
 foreach($key in $registrationBefore.Keys){
  $values=$registrationBefore[$key]
  if(Test-Path $key){$native=Get-Item $key;foreach($name in $native.GetValueNames()){if(!$values.ContainsKey($name)){Remove-ItemProperty -LiteralPath $key -Name $name}}}
  if(!$values.Count -and (Test-Path $key) -and !(Get-Item $key).SubKeyCount){Remove-Item -LiteralPath $key}
  if($values.Count){New-Item -Path $key -Force|Out-Null;foreach($name in $values.Keys){New-ItemProperty -LiteralPath $key -Name $name -Value $values[$name].Value -PropertyType $values[$name].Kind -Force|Out-Null}}
 }
 foreach($path in $shortcutBefore.Keys){
  if($null -ne $shortcutBefore[$path]){[IO.File]::WriteAllBytes($path,$shortcutBefore[$path])}
  elseif(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}
 }
 if(Test-Path $runBefore){if($null -ne $startupBefore){Set-ItemProperty $runBefore -Name DS5Link -Value $startupBefore}else{Remove-ItemProperty $runBefore -Name DS5Link -ErrorAction SilentlyContinue}}
 throw $failure
}
try{Update-AppBackupRetention $install (Join-Path $data 'backups.json') $(if($wasInstalled){$backup}else{''})}catch{Write-Warning ('Backup retention deferred: '+$_.Exception.Message)}
if($InstallerMode){
 Stop-Transcript | Out-Null
 if(Test-Path (Join-Path $data 'reboot-required.txt')){exit 3010}
 exit 0
}
