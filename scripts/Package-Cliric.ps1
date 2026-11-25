#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root 'artifacts/cliric'
$assembly = Join-Path $app 'Cliric.dll'
$version = [Reflection.AssemblyName]::GetAssemblyName($assembly).Version.ToString(3)
$output = Join-Path $root "artifacts/Cliric-$version-win-x64.zip"
$node = (Get-Command node.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$nodeVersion = & $node --version
if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v\d+\.\d+\.\d+$') { throw 'Portable Node version unavailable.' }
$mpvPath = Join-Path $root 'tools/ncm-cli-0.1.7/mpv'
$mpvVersion = & (Join-Path $mpvPath 'mpv.exe') --no-config --version | Select-Object -First 1
if ($mpvVersion -notmatch '-g([0-9a-f]{7,40})\b') { throw 'mpv source revision unavailable.' }
$mpvRevision = $Matches[1]
$nodeLicense = (Invoke-WebRequest "https://raw.githubusercontent.com/nodejs/node/$nodeVersion/LICENSE" -TimeoutSec 25).Content
$mpvLicenseRoot = "https://raw.githubusercontent.com/mpv-player/mpv/$mpvRevision"
$licenseNames = 'Copyright', 'LICENSE.GPL', 'LICENSE.LGPL'
$mpvNotices = @{}
foreach ($name in $licenseNames) {
    $mpvNotices[$name] = (Invoke-WebRequest "$mpvLicenseRoot/$name" -TimeoutSec 25).Content
}

$partial = "$output.partial-$([Guid]::NewGuid().ToString('N'))"
$zip = [IO.Compression.ZipFile]::Open($partial, [IO.Compression.ZipArchiveMode]::Create)
function Add-File([string]$path, [string]$name) {
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $path, "Cliric/$name", [IO.Compression.CompressionLevel]::Optimal)
}
function Add-Tree([string]$source, [string]$prefix) {
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\', '/')
        if ($relative -notmatch '^(UserData/)|\.pdb$') { Add-File $file.FullName "$prefix/$relative" }
    }
}
function Add-Text([string]$name, [string]$content) {
    $entry = $zip.CreateEntry("Cliric/$name")
    $writer = [IO.StreamWriter]::new($entry.Open())
    try { $writer.WriteLine($content) }
    finally { $writer.Dispose() }
}

try {
    Add-Tree $app 'artifacts/cliric'
    Add-Tree (Join-Path $root 'tools/netease-client/node_modules') 'tools/netease-client/node_modules'
    Add-Tree (Join-Path $root 'tools/ncm-cli-0.1.7/node_modules') 'tools/ncm-cli-0.1.7/node_modules'
    Add-Tree $mpvPath 'tools/ncm-cli-0.1.7/mpv'
    Add-File (Join-Path $root 'Start-Cliric.cmd') 'Start-Cliric.cmd'
    Add-File (Join-Path $root 'LICENSE') 'LICENSE'
    Add-File (Join-Path $root 'THIRD_PARTY.md') 'THIRD_PARTY.md'
    Add-File $node 'tools/node/node.exe'
    Add-Text 'tools/node/LICENSE.txt' $nodeLicense
    foreach ($name in $licenseNames) { Add-Text "tools/ncm-cli-0.1.7/mpv/$name" $mpvNotices[$name] }
    Add-Text 'tools/ncm-cli-0.1.7/mpv/SOURCE.txt' "mpv source revision $mpvRevision`nhttps://github.com/mpv-player/mpv/tree/$mpvRevision`nWindows build scripts https://github.com/shinchiro/mpv-winbuild-cmake"
    Add-Text 'NetEase-OpenPlatform.cmd' @'
@echo off
setlocal
set "CLIRIC_NCM=%~dp0tools\ncm-cli-0.1.7"
set "USERPROFILE=%CLIRIC_NCM%\profile"
set "APPDATA=%USERPROFILE%\Roaming"
set "LOCALAPPDATA=%USERPROFILE%\Local"
set "NETEASE_APP_ID="
set "NETEASE_PRIVATE_KEY="
set "NETEASE_APP_SECRET="
set "PATH=%CLIRIC_NCM%\mpv;%PATH%"
"%~dp0tools\node\node.exe" "%CLIRIC_NCM%\node_modules\@music163\ncm-cli\dist\index.js" %*
'@
    Add-Text '使用说明.txt' @'
Cliric 便携版

在 Windows 11 x64 上完整解压，双击 Start-Cliric.cmd。把整个目录留在可写位置。
从侧栏添加本地音乐和歌词，或在右上角扫码登录网易云。切换电脑后重新扫码。
开放平台可用 NetEase-OpenPlatform.cmd configure 和 NetEase-OpenPlatform.cmd login 在本机配置。
应用自带现代背景。需要原版背景时，程序从 artifacts/reference-release-4.4.2/payload 读取本地渲染组件。
新机的设置保存在 artifacts/cliric/UserData/Profile，可按需迁移 settings.json 和 player-typography.json。
仓库 https://github.com/AkiraNyx/cliric
'@
}
finally { $zip.Dispose() }
[IO.File]::Move($partial, $output, $true)
Write-Output $output
