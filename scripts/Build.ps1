param([ValidateSet('Debug','Release')][string]$Configuration='Release',[switch]$Test,[switch]$Publish,[switch]$OfflineVerify)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_CLI_HOME=Join-Path $root 'tools\dotnet-home'
$env:NUGET_PACKAGES=Join-Path $root 'tools\nuget'
$dotnet=Join-Path $root 'tools\dotnet\dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnet)){ & (Join-Path $PSScriptRoot 'Setup-Sdk.ps1') }
Set-Location $root
if($Test){
  & $dotnet run --project tests/LyricReplica.Tests/LyricReplica.Tests.csproj -c $Configuration
  if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
}
if(Test-Path -LiteralPath 'src/LyricReplica.App/LyricReplica.App.csproj'){
  $buildArgs=@('build','src/LyricReplica.App/LyricReplica.App.csproj','-c',$Configuration,'--artifacts-path','artifacts/build')
  if($OfflineVerify){$buildArgs+=@('-o','artifacts/offline-build')}
  & $dotnet @buildArgs
  if($LASTEXITCODE -ne 0){throw 'WPF build failed'}
  if($OfflineVerify){
    & $dotnet artifacts/offline-build/LyricReplica.dll --verify-offscreen
    if($LASTEXITCODE -ne 0){throw 'Offscreen verification failed; see artifacts/offline-build/UserData/Offscreen/failure.txt'}
  }
  if($Publish){
    $delivery=Join-Path $root 'artifacts/current'
    if(Get-Process -Name LyricReplica -ErrorAction SilentlyContinue|Where-Object Path -eq (Join-Path $delivery 'LyricReplica.exe')){throw 'Close the current LyricReplica window before rebuilding its running package.'}
    & $dotnet publish src/LyricReplica.App/LyricReplica.App.csproj -c $Configuration -r win-x64 --self-contained true --artifacts-path artifacts/publish-build -o $delivery
    if($LASTEXITCODE -ne 0){throw 'Publish failed'}
  }
}
