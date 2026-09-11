param(
    [string]$Version = "v0.14.0-beta.3",
    [string]$PublishDir = ""
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$TempBase = "$Root\_temp_zip"
$TempDir = "$TempBase\MATR"
$Platform = "win-x64"
$ZipFile = "$Root\MATR-$Version-$Platform.zip"
$SourceDir = if ([string]::IsNullOrWhiteSpace($PublishDir)) { $Root } else { (Resolve-Path $PublishDir).Path }

# Clean old temp dir and zip
if (Test-Path $TempBase) { Remove-Item -Recurse -Force $TempBase }
if (Test-Path $ZipFile) { Remove-Item -Force $ZipFile }

# Create temp dirs
New-Item -ItemType Directory -Force -Path "$TempDir\assets" | Out-Null

# 发布产物缺失时直接失败，避免打出缺少程序本体的残废包
foreach ($name in @("MATR.exe", "MATR.dll", "libloader.dll")) {
    if (-not (Test-Path "$SourceDir\$name")) {
        throw "发布产物缺失: $SourceDir\$name（请先执行 dotnet publish）"
    }
}

# Copy files
Copy-Item "$SourceDir\MATR.exe" $TempDir
Copy-Item "$SourceDir\MATR.dll" $TempDir
Copy-Item "$SourceDir\MATR.deps.json" $TempDir
Copy-Item "$SourceDir\MATR.runtimeconfig.json" $TempDir
Copy-Item "$SourceDir\libloader.dll" $TempDir
Copy-Item "$Root\DependencySetup_*.bat" $TempDir
Copy-Item "$Root\README.md" $TempDir
Copy-Item "$Root\LICENSE" $TempDir
Copy-Item "$Root\assets\interface.json" "$TempDir\assets\interface.json"
Copy-Item "$SourceDir\runtimes" -Recurse -Destination "$TempDir\runtimes"
$KeepDirs = @("libs", "plugins", $Platform)
Get-ChildItem "$TempDir\runtimes" -Directory | ForEach-Object {
    if ($KeepDirs -notcontains $_.Name) { Remove-Item -Recurse -Force $_.FullName }
}

# 发布目录中的根级 libs 是兼容输入；发布包统一使用 runtimes/libs 布局。
if (Test-Path "$SourceDir\libs") {
    New-Item -ItemType Directory -Force -Path "$TempDir\runtimes\libs" | Out-Null
    Get-ChildItem -LiteralPath "$SourceDir\libs" -Force | ForEach-Object {
        $Destination = Join-Path "$TempDir\runtimes\libs" $_.Name
        if ($_.PSIsContainer -and (Test-Path $Destination)) {
            Copy-Item (Join-Path $_.FullName '*') -Recurse -Destination $Destination -Force
        }
        else {
            Copy-Item $_.FullName -Recurse -Destination $Destination -Force
        }
    }
}

# 发布目录 runtimes/libs 由本次构建生成，优先级高于兼容输入的根级 libs。
# 否则根级目录残留的旧 MFAAvalonia.Core.dll 会覆盖刚构建的程序集。
if (Test-Path "$SourceDir\runtimes\libs") {
    Copy-Item "$SourceDir\runtimes\libs\*" -Recurse -Destination "$TempDir\runtimes\libs" -Force
}
$AgentTarget = "$TempDir\runtimes\libs\MaaAgentBinary"
if (Test-Path $AgentTarget) {
    Remove-Item -Recurse -Force $AgentTarget
}
if (Test-Path "$SourceDir\plugins") {
    Copy-Item "$SourceDir\plugins" -Recurse -Destination "$TempDir\plugins"
}

# 移除 libs 中与 win-x64/native 重复的原生库（如 libSkiaSharp.dll）。
Get-ChildItem "$TempDir\runtimes\libs" -File | ForEach-Object {
    if (Test-Path "$TempDir\runtimes\$Platform\native\$($_.Name)") {
        Remove-Item -Force $_.FullName
    }
}

Copy-Item "$Root\assets\resource" -Recurse -Destination "$TempDir\assets\resource"

# 发布包不包含运行时配置和刀帐个人数据；这些文件只存在于开发区的 config/ 中。
if (Test-Path "$TempDir\config") { Remove-Item -Recurse -Force "$TempDir\config" }
if (Test-Path "$TempDir\assets\config") { Remove-Item -Recurse -Force "$TempDir\assets\config" }
if (Test-Path "$TempDir\assets\resource\config") { Remove-Item -Recurse -Force "$TempDir\assets\resource\config" }
if (Test-Path "$TempDir\assets\resource\temp") { Remove-Item -Recurse -Force "$TempDir\assets\resource\temp" }
if (Test-Path "$TempDir\assets\resource\backup") { Remove-Item -Recurse -Force "$TempDir\assets\resource\backup" }
if (Test-Path "$TempDir\assets\resource\base\image\unused") { Remove-Item -Recurse -Force "$TempDir\assets\resource\base\image\unused" }

# Package (compress temp dir contents directly, no wrapper folder)
Compress-Archive -Path "$TempDir\*" -DestinationPath $ZipFile -Force

# Cleanup
Remove-Item -Recurse -Force "$Root\_temp_zip"

Write-Host "打包完成: $ZipFile"
