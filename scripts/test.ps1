[CmdletBinding()]
param([ValidateSet('Release','Debug')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$ctest="${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\ctest.exe"
& $ctest --test-dir (Join-Path $root "artifacts\native-$Configuration") --output-on-failure
if ($LASTEXITCODE) { throw 'Native tests failed' }
& (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') run --project (Join-Path $root 'tests\DS5Link.Tests\DS5Link.Tests.csproj') -c $Configuration -p:Platform=AnyCPU
if($LASTEXITCODE){throw 'Managed protocol and diagnostics tests failed'}
& (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') run --project (Join-Path $root 'tests\DS5Link.Tests\DS5Link.Tests.csproj') -c $Configuration -p:Platform=AnyCPU --no-build -- --adversarial-review
if($LASTEXITCODE){throw 'Adversarial regression tests failed'}
& (Join-Path $PSScriptRoot 'review-reboot-marker.ps1')

& (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') build (Join-Path $root 'src\DS5Link.Avalonia\DS5Link.Avalonia.csproj') -c $Configuration -p:Platform=AnyCPU
if($LASTEXITCODE){throw 'Avalonia build failed'}
$ui=Join-Path $root "src\DS5Link.Avalonia\bin\$Configuration\net10.0-windows\win-x64\DS5Link.exe"
$process=Start-Process $ui -ArgumentList '--ui-qa' -PassThru -Wait
if($process.ExitCode -ne 0){throw 'Avalonia layout/state tests failed'}

& (Join-Path $PSScriptRoot "test-dependencies.ps1")

& (Join-Path $PSScriptRoot 'test-owned-files.ps1')
