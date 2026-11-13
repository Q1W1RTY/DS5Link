[CmdletBinding(SupportsShouldProcess=$true)]
param([switch]$Apply)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$releaseRoot=Join-Path $projectRoot 'artifacts\releases'
if(!(Test-Path -LiteralPath $releaseRoot)){return}
# Only recognized, hash-verified local releases participate. Unknown folders stay intact.
$releases=@(foreach($dir in Get-ChildItem -LiteralPath $releaseRoot -Directory){
 if($dir.Name -notmatch '^DS5Link-\d+\.\d+\.\d+(?:-.+)?$'){continue}
 $metadataPath=Join-Path $dir.FullName 'release.json'
 $hashPath=Join-Path $dir.FullName 'SHA256.txt'
 $verificationPath=Join-Path $dir.FullName 'verification.json'
 if(!(Test-Path $metadataPath) -or !(Test-Path $hashPath) -or !(Test-Path $verificationPath)){continue}
 $metadata=Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
 if(!(Get-Content -LiteralPath $verificationPath -Raw | ConvertFrom-Json).ok){continue}
 $exe=Join-Path $dir.FullName 'DS5Link-Setup.exe'
 if(!(Test-Path -LiteralPath $exe)){continue}
 if((Get-Content -LiteralPath $hashPath -First 1).Split(' ')[0] -ne (Get-FileHash -LiteralPath $exe).Hash){throw "Installer hash mismatch: $exe"}
 $builtAt=$metadata.builtAt
 if($builtAt -isnot [datetime] -and $builtAt -isnot [DateTimeOffset]){
  $builtAt=[DateTimeOffset]::Parse($builtAt,[Globalization.CultureInfo]::InvariantCulture)
 }
 [pscustomobject]@{Directory=$dir;BuiltAt=[DateTimeOffset]$builtAt;Version=$metadata.version}
}) | Sort-Object BuiltAt -Descending
if($releases.Count -le 2){Write-Output 'At most two verified releases; nothing to prune.';return}
$newest=$releases[0]
if($newest.Version -ne (Get-Content (Join-Path $projectRoot 'VERSION') -Raw).Trim()){throw 'Newest release does not match current VERSION'}
$qaPath=Join-Path $newest.Directory.FullName 'qa\result.json'
if(!(Test-Path $qaPath) -or !(Get-Content $qaPath -Raw | ConvertFrom-Json).ok){throw 'Newest release must pass full installer QA first'}
$targets=@($releases | Select-Object -Skip 2)
$running=@(Get-CimInstance Win32_Process | Where-Object ExecutablePath | Select-Object -ExpandProperty ExecutablePath)
foreach($release in $targets){
 $path=[IO.Path]::GetFullPath($release.Directory.FullName)
 if([IO.Path]::GetDirectoryName($path) -ne $releaseRoot){throw 'Deletion must be a direct child of this project release folder'}
 $ancestors=@((Get-Item -LiteralPath (Join-Path $projectRoot 'artifacts')),(Get-Item -LiteralPath $releaseRoot),$release.Directory)
 if(@($ancestors | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count -or @(Get-ChildItem -LiteralPath $path -Recurse -Force -Attributes ReparsePoint).Count){throw 'Reparse points are not eligible for cleanup'}
 foreach($exe in $running){if($exe.StartsWith($path+'\',[StringComparison]::OrdinalIgnoreCase)){throw "Running executable uses $path"}}
}
$before=(Get-ChildItem -LiteralPath $projectRoot -File -Recurse -Force | Measure-Object Length -Sum).Sum
foreach($release in $targets){
 $path=$release.Directory.FullName
 if($Apply -and $PSCmdlet.ShouldProcess($path,'Remove superseded verified release')){Remove-Item -LiteralPath $path -Recurse -Force}
 else{Write-Output "Would remove: $path"}
}
$after=(Get-ChildItem -LiteralPath $projectRoot -File -Recurse -Force | Measure-Object Length -Sum).Sum
[pscustomobject]@{Applied=[bool]$Apply;Retained=@($releases | Select-Object -First 2 | ForEach-Object {$_.Directory.Name});ReclaimedGiB=[math]::Round(($before-$after)/1GB,3);ProjectGiB=[math]::Round($after/1GB,3)} | ConvertTo-Json
