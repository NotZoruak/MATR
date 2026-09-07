param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$PublishDir
)

$ErrorActionPreference = 'Stop'

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Platform = "macos-arm64"
$StagingDir = Join-Path $Root '_temp_macos'
$AppDir = Join-Path $StagingDir 'MATR.app'
$MacOsDir = Join-Path $AppDir "Contents\MacOS"
$ResourcesDir = Join-Path $AppDir "Contents\Resources"
$ZipFile = Join-Path $Root "MATR-$Version-$Platform.zip"

function Get-BundleVersion([string]$SourceVersion) {
    $value = $SourceVersion.TrimStart('v', 'V') -replace '[-+].*$', ''
    if ($value -match '^\d+(\.\d+){0,2}$') { return $value }
    return '0.0.0'
}

function New-UnixZip([string]$SourceDir, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [System.IO.File]::Open($Destination, [System.IO.FileMode]::Create)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            Get-ChildItem -LiteralPath $SourceDir -Recurse -Force | ForEach-Object {
                $relative = $_.FullName.Substring($SourceDir.Length + 1).Replace('\', '/')
                if ($_.PSIsContainer) {
                    $entry = $archive.CreateEntry("$relative/")
                    $entry.ExternalAttributes = 0x41ED0000
                } else {
                    $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
                    $entry.ExternalAttributes = 0x81ED0000
                    $input = [System.IO.File]::OpenRead($_.FullName)
                    $output = $entry.Open()
                    try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
                }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

if (Test-Path $StagingDir) { Remove-Item -LiteralPath $StagingDir -Recurse -Force }
if (Test-Path $ZipFile) { Remove-Item -LiteralPath $ZipFile -Force }

New-Item -ItemType Directory -Force -Path $MacOsDir, $ResourcesDir, (Join-Path $MacOsDir 'assets') | Out-Null
Copy-Item (Join-Path $PublishDir '*') -Destination $MacOsDir -Recurse -Force
$AgentTarget = Join-Path $MacOsDir 'MaaAgentBinary'
if (Test-Path $AgentTarget) {
    Remove-Item -LiteralPath $AgentTarget -Recurse -Force
}
$RuntimeAgentTarget = Join-Path $MacOsDir 'runtimes\libs\MaaAgentBinary'
if (Test-Path $RuntimeAgentTarget) {
    Remove-Item -LiteralPath $RuntimeAgentTarget -Recurse -Force
}
$BundleExecutable = Join-Path $MacOsDir 'MATR'
if (-not (Test-Path -LiteralPath $BundleExecutable)) {
    throw "macOS 发布产物缺少入口程序: $BundleExecutable"
}
$SourceInterface = Join-Path $Root 'assets\interface.json'
$SourceResource = Join-Path $Root 'assets\resource'
$TargetInterface = Join-Path $MacOsDir 'assets\interface.json'
$TargetResource = Join-Path $MacOsDir 'assets\resource'
Copy-Item -LiteralPath $SourceInterface -Destination $TargetInterface -Force
Copy-Item -LiteralPath $SourceResource -Destination $TargetResource -Recurse -Force
if (-not (Test-Path -LiteralPath $TargetInterface)) {
    throw "macOS 打包未复制资源接口文件: $TargetInterface"
}

$SourceResourceFileCount = @(Get-ChildItem -LiteralPath $SourceResource -Recurse -File).Count
$TargetResourceFileCount = @(Get-ChildItem -LiteralPath $TargetResource -Recurse -File).Count
if ($TargetResourceFileCount -ne $SourceResourceFileCount) {
    throw "macOS 打包资源文件数量不一致: 源=$SourceResourceFileCount, 目标=$TargetResourceFileCount"
}
Copy-Item (Join-Path $Root 'README.md') -Destination $MacOsDir -Force
Copy-Item (Join-Path $Root 'LICENSE') -Destination $MacOsDir -Force

@('config', 'debug', 'temp', 'backup') | ForEach-Object {
    $path = Join-Path $MacOsDir $_
    if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

$bundleVersion = Get-BundleVersion $Version
@"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
    <key>CFBundleName</key><string>MATR</string>
    <key>CFBundleDisplayName</key><string>MATR</string>
    <key>CFBundleIdentifier</key><string>com.notzoruak.matr</string>
    <key>CFBundleVersion</key><string>$bundleVersion</string>
    <key>CFBundleShortVersionString</key><string>$bundleVersion</string>
    <key>CFBundleExecutable</key><string>MATR</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
</dict></plist>
"@ | Set-Content -LiteralPath (Join-Path $AppDir 'Contents\Info.plist') -Encoding utf8NoBOM

New-UnixZip $StagingDir $ZipFile
Remove-Item -LiteralPath $StagingDir -Recurse -Force
Write-Host "macOS 打包完成: $ZipFile"
