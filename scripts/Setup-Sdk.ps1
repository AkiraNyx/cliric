$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$toolsRoot = Join-Path $root 'tools'
$sdkRoot = Join-Path $toolsRoot 'dotnet'
$version = '10.0.403'
$expected = '7a114c318fa7f1d9822991b534b2814d0e78386ceda816b371f5141acefe741d87ccb606ede06cadac8f9ac5070e32b708a67bbd1619ff5e5a7858a27874ef48'
$url = 'https://dotnetcli.azureedge.net/dotnet/Sdk/10.0.403/dotnet-sdk-10.0.403-win-x64.zip'
if (Test-Path -LiteralPath (Join-Path $sdkRoot 'sdk\10.0.403')) {
    & (Join-Path $sdkRoot 'dotnet.exe') --version
    exit $LASTEXITCODE
}
New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
$archive = Join-Path $toolsRoot ('dotnet-sdk-' + $version + '.zip')
if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash.ToLowerInvariant() -ne $expected) {
    & curl.exe --fail --location --retry 2 --connect-timeout 20 --max-time 600 --output $archive $url
    if ($LASTEXITCODE -ne 0) { throw 'SDK download failed' }
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash.ToLowerInvariant() -ne $expected) { throw 'SDK archive SHA-512 mismatch' }
New-Item -ItemType Directory -Path $sdkRoot -Force | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $sdkRoot -Force
& (Join-Path $sdkRoot 'dotnet.exe') --info
if ($LASTEXITCODE -ne 0) { throw 'Project SDK did not start' }
