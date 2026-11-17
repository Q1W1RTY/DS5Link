[CmdletBinding()]
param([string]$PackageDirectory,[string]$OutputDirectory,[string]$CandidateNativeDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$version=(Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if(!$PackageDirectory){$PackageDirectory=(Get-Content (Join-Path $root 'artifacts\packages\LATEST.txt') -Raw).Trim() -replace '\.zip$',''}
$PackageDirectory=[IO.Path]::GetFullPath($PackageDirectory)
if(!$OutputDirectory){$OutputDirectory=Join-Path $root (('artifacts\releases\DS5Link-'+$version+'-')+(Get-Date -Format yyyyMMdd-HHmmss))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path $OutputDirectory){throw 'Use a new output directory; release files are not overwritten.'}
# Release the hardware-tested native baseline, never the experimental build directory.
$expectedNative='F22337DD797FA8303E4474B833F471B0DA4EA6D4C0C90BF1B8B1823F518524ED'
if((Get-FileHash (Join-Path $PackageDirectory 'app\engine\vdsd.exe')).Hash -ne $expectedNative){throw 'This release profile only accepts the hardware-tested 064430 native baseline.'}
foreach($line in Get-Content (Join-Path $PackageDirectory 'files.sha256')){
 if(!$line.Trim()){continue}
 if($line -notmatch '^([A-Fa-f0-9]{64})  (.+)$'){throw 'Malformed baseline hash manifest'}
 $expected=$Matches[1];$path=[IO.Path]::GetFullPath((Join-Path $PackageDirectory $Matches[2]))
 if(!$path.StartsWith($PackageDirectory+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid baseline manifest path'}
 if((Get-FileHash -LiteralPath $path).Hash -ne $expected){throw "Baseline modified: $path"}
}
New-Item -ItemType Directory $OutputDirectory | Out-Null
$payload=Join-Path $OutputDirectory 'payload';New-Item -ItemType Directory $payload | Out-Null
Copy-Item (Join-Path $PackageDirectory 'app') $payload -Recurse
foreach($culture in @('cs','de','es','fr','it','ja','ko','pl','pt-BR','tr','zh-Hans','zh-Hant')){
 $culturePath=Join-Path $payload ('app\'+$culture)
 if(Test-Path -LiteralPath $culturePath){foreach($file in @(Get-ChildItem -LiteralPath $culturePath -File)){if($file.Name.EndsWith('.resources.dll')){Remove-Item -LiteralPath $file.FullName}};if(!@(Get-ChildItem -LiteralPath $culturePath -Force).Count){[IO.Directory]::Delete($culturePath,$false)}}
}
# Publish only the UI; the hardware-tested native files above are retained.
& (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') publish (Join-Path $root 'src\DS5Link.Avalonia\DS5Link.Avalonia.csproj') -c Release -r win-x64 --self-contained true -p:Platform=AnyCPU -p:SatelliteResourceLanguages=ru -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $payload 'app')
if($LASTEXITCODE){throw 'UI publish failed'}
foreach($legacyFile in @('DS5Link.pdb','DSEmu.exe','DSEmu.dll','DSEmu.deps.json','DSEmu.runtimeconfig.json','DSEmu.pdb','DS5Emu.exe','DS5Emu.dll','DS5Emu.deps.json','DS5Emu.runtimeconfig.json','DS5Emu.pdb')){
 $legacyPath=Join-Path $payload ('app\'+$legacyFile)
 if(Test-Path -LiteralPath $legacyPath){Remove-Item -LiteralPath $legacyPath}
}
if((Get-FileHash (Join-Path $payload 'app\engine\vdsd.exe')).Hash -ne $expectedNative){throw 'Native baseline changed during UI publish'}
if($CandidateNativeDirectory){
 $CandidateNativeDirectory=[IO.Path]::GetFullPath($CandidateNativeDirectory)
 foreach($file in @('vdsd.exe','vdsctl.exe','opus.dll')){Copy-Item -LiteralPath (Join-Path $CandidateNativeDirectory $file) -Destination (Join-Path $payload ('app\engine\'+$file)) -Force}
}
$dependencyPackage=(Get-Content (Join-Path $root 'artifacts\packages\LATEST.txt') -Raw).Trim() -replace '\.zip$',''
Copy-Item (Join-Path $dependencyPackage 'dependencies') $payload -Recurse
Copy-Item (Join-Path $dependencyPackage 'dependencies.json') $payload
Copy-Item (Join-Path $root 'third_party\ViGEmClient\LICENSE') (Join-Path $payload 'app\licenses\ViGEmClient-MIT.txt')
Copy-Item (Join-Path $root 'third_party\NAudio\LICENSE') (Join-Path $payload 'app\licenses\NAudio-MIT.txt')
Copy-Item (Join-Path $root 'third_party\TriggerEffects\LICENSE') (Join-Path $payload 'app\licenses\TriggerEffects-MIT.txt')
if($CandidateNativeDirectory){
 $engineBuild=[ordered]@{
  version=$version
  upstream=(git -C (Join-Path $root 'engine\vds') rev-parse HEAD)
  patchSha256=(Get-FileHash (Join-Path $root 'patches\vds\0001-ds5link-bootstrap.patch')).Hash
  nativeSha256=(Get-FileHash (Join-Path $payload 'app\engine\vdsd.exe')).Hash
 }
 $engineBuild | ConvertTo-Json | Set-Content (Join-Path $payload 'engine-build.json') -Encoding utf8
}else{
 Copy-Item (Join-Path $PackageDirectory 'build.json') (Join-Path $payload 'engine-build.json')
}
Copy-Item (Join-Path $root 'installer\Install.ps1'),(Join-Path $root 'installer\Uninstall.ps1'),(Join-Path $root 'installer\InstallPaths.ps1'),(Join-Path $root 'installer\OwnedFiles.ps1'),(Join-Path $root 'installer\Dependencies.ps1') $payload
Copy-Item (Join-Path $root 'docs\user\INSTALL.md') (Join-Path $payload 'README-RU.md')
Copy-Item (Join-Path $root 'docs\user\INSTALL.md') (Join-Path $OutputDirectory 'README-RU.md')
foreach($dependency in (Get-Content (Join-Path $payload 'dependencies.json') -Raw | ConvertFrom-Json)){
 $file=Join-Path $payload ('dependencies\'+$dependency.file)
 if((Get-FileHash $file).Hash -ne $dependency.sha256){throw 'Dependency hash mismatch'}
 if((Get-AuthenticodeSignature $file).Status -ne 'Valid'){throw 'Dependency signature invalid'}
}
$release=[ordered]@{version=$version;sourceCommit=(git -C $root rev-parse HEAD);sourceDirty=[bool](git -C $root status --porcelain);engineRevision=(git -C (Join-Path $root 'engine\vds') rev-parse HEAD);patchSha256=(Get-FileHash (Join-Path $root 'patches\vds\0001-ds5link-bootstrap.patch')).Hash;builtAt=(Get-Date).ToString('o');enginePackage=(Split-Path $PackageDirectory -Leaf);nativeSha256=(Get-FileHash (Join-Path $payload 'app\engine\vdsd.exe')).Hash;candidate=[bool]$CandidateNativeDirectory;hardwareAccepted=(!$CandidateNativeDirectory);uiSha256=(Get-FileHash (Join-Path $payload 'app\DS5Link.dll')).Hash;iconSha256=(Get-FileHash (Join-Path $root 'src\DS5Link.App\Assets\DS5Link.ico')).Hash;experimentalQueue=$false;bootstrapStyleSha256=(Get-FileHash (Join-Path $root 'installer\SetupStyle.cs')).Hash;bootstrapSourceSha256=(Get-FileHash (Join-Path $root 'installer\Setup.cs')).Hash;installerSourceSha256=(Get-FileHash (Join-Path $root 'installer\Install.ps1')).Hash}
$release|ConvertTo-Json|Set-Content (Join-Path $payload 'release.json') -Encoding utf8
Copy-Item (Join-Path $payload 'release.json') $OutputDirectory
Get-ChildItem $payload -File -Recurse | Sort-Object FullName | ForEach-Object {'{0}  {1}' -f (Get-FileHash $_.FullName).Hash,$_.FullName.Substring($payload.Length+1)} | Set-Content (Join-Path $payload 'files.sha256') -Encoding utf8
$zip=Join-Path $OutputDirectory 'payload.zip'
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -CompressionLevel Optimal
$compiler=Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $OutputDirectory 'DS5Link-Setup.exe'
$versionSource=Join-Path $OutputDirectory 'SetupVersion.cs'
('[assembly:System.Reflection.AssemblyVersion("'+$version+'.0")]')|Set-Content $versionSource -Encoding utf8
$manifest=Join-Path $OutputDirectory 'Setup.manifest'
([IO.File]::ReadAllText((Join-Path $root 'installer\Setup.manifest')) -replace 'version="0\.0\.0\.0"',('version="'+$version+'.0"'))|Set-Content $manifest -Encoding utf8
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$exe" "/win32icon:$root\src\DS5Link.App\Assets\DS5Link.ico" "/win32manifest:$manifest" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$zip,DS5Link.Payload" (Join-Path $root 'installer\Setup.cs') (Join-Path $root 'installer\SetupStyle.cs') $versionSource
if($LASTEXITCODE){throw 'Bootstrap compilation failed'}
$verification=Join-Path $OutputDirectory 'verification.json'
$process=Start-Process $exe -ArgumentList @('--verify-only',('"'+$verification+'"')) -WindowStyle Hidden -PassThru -Wait
if($process.ExitCode -ne 0 -or !(Get-Content $verification -Raw|ConvertFrom-Json).ok){throw 'Embedded payload verification failed'}
'{0}  DS5Link-Setup.exe' -f (Get-FileHash $exe).Hash | Set-Content (Join-Path $OutputDirectory 'SHA256.txt') -Encoding ascii
Remove-Item -LiteralPath $zip -Force
Write-Host "Release: $exe"
Write-Host 'Payload verified without installing or changing the running app.'
