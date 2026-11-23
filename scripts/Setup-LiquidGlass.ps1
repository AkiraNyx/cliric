$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$packageFeed = Join-Path $root 'tools/capsule-preview-packages'
$null = New-Item -ItemType Directory -Path $packageFeed -Force
$packages = @{
    'LiquidGlassEngineering.Core.1.0.2.nupkg' = 'FD559134107639B039D71731CFC8A57DFE7E812831EB3F794D88DC59F7FCC7CE'
    'LiquidGlassEngineering.Wpf.1.0.2.nupkg' = 'C3A19B4225D4C7FC54C929EFF18E03F6E84B346DC4FABE066B626CDCD65FD60F'
}
foreach ($name in $packages.Keys) {
    $target = Join-Path $packageFeed $name
    if ((Test-Path -LiteralPath $target -PathType Leaf) -and (Get-FileHash -LiteralPath $target).Hash -eq $packages[$name]) { continue }
    $download = "$target.partial"
    $release = 'https://github.com/rookasrudzianskas/liquid-glass-mac-windows-ios-android/releases/download/1.0.2'
    Invoke-WebRequest -Uri "$release/$name" -OutFile $download -TimeoutSec 30
    if ((Get-FileHash -LiteralPath $download).Hash -ne $packages[$name]) { throw "Package checksum mismatch: $name" }
    Move-Item -LiteralPath $download -Destination $target -Force
}
