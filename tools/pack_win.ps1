param(
[string]$Version = "v0.15.6",
    [string]$PublishDir = ""
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$TempBase = "$Root\_temp_zip"
$TempDir = "$TempBase\MATR"
$Platform = "win-x64"
$ZipFile = "$Root\MATR-$Version-$Platform.zip"
$SourceDir = if ([string]::IsNullOrWhiteSpace($PublishDir)) { $Root } else { (Resolve-Path $PublishDir).Path }
$CpuPatchRuntime = Join-Path $Root 'runtimes\patches\win-x64\native'
$LegacyCpuPatchRuntime = Join-Path $Root 'runtimes\win-x64\native'
$ExpectedCpuPatchHashes = [ordered]@{
    'MaaFramework.dll' = '06E7A49B431EC55375D8552200E943750C328703198DDFE03AD731BE57547653'
    'fastdeploy_ppocr_maa.dll' = '671FEAEEAA4D8F34719B8E75F279C810CC80C0DB37F100778975F75D12C2F192'
    'onnxruntime_maa.dll' = '29C654B2B6BA9EED0FC5FC94DD43407B2128A01CA02A8D32AB7AA6EFDFFC03E1'
    'opencv_world4_maa.dll' = '34524A1B28B343E16B8016F44C52046358576BE0E84C393D17DE1146502DF555'
}
foreach ($name in $ExpectedCpuPatchHashes.Keys) {
    $path = Join-Path $CpuPatchRuntime $name
    $actualHash = if (Test-Path -LiteralPath $path -PathType Leaf) {
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    else {
        ''
    }
    if ($actualHash -ne $ExpectedCpuPatchHashes[$name]) {
        $legacyPath = Join-Path $LegacyCpuPatchRuntime $name
        if (Test-Path -LiteralPath $legacyPath -PathType Leaf) {
            $legacyHash = (Get-FileHash -LiteralPath $legacyPath -Algorithm SHA256).Hash
            if ($legacyHash -eq $ExpectedCpuPatchHashes[$name]) {
                New-Item -ItemType Directory -Force -Path $CpuPatchRuntime | Out-Null
                Copy-Item -LiteralPath $legacyPath -Destination $path -Force
                $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
            }
        }
    }
    if ($actualHash -ne $ExpectedCpuPatchHashes[$name]) {
        throw "MaaFW 5.16.0 补丁依赖校验失败：$name；缓存路径：$path；兼容路径：$(Join-Path $LegacyCpuPatchRuntime $name)；实际摘要：$actualHash。请按 tools/patches 中的说明构建并放置。"
    }
}

$SourceFramework = Join-Path $SourceDir 'runtimes\win-x64\native\MaaFramework.dll'
if (-not (Test-Path -LiteralPath $SourceFramework -PathType Leaf)) {
    throw "发布目录缺少 MaaFramework.dll：$SourceFramework"
}
$SourceFrameworkHash = (Get-FileHash -LiteralPath $SourceFramework -Algorithm SHA256).Hash
if ($SourceFrameworkHash -notin @('434809C1F4504D3A39B4E24C609A4F1DAD502418AAF30326F3EB480CA218237E', $ExpectedCpuPatchHashes['MaaFramework.dll'])) {
    throw '发布目录不是预期的官方 5.16.0 或当前四线程补丁构建，拒绝混入其他版本控制器。'
}

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
$PackageNative = Join-Path $TempDir 'runtimes\win-x64\native'
foreach ($name in $ExpectedCpuPatchHashes.Keys) {
    $packagePath = Join-Path $PackageNative $name
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Windows 发布目录缺少原生运行库：$packagePath"
    }
    Copy-Item -LiteralPath (Join-Path $CpuPatchRuntime $name) -Destination $packagePath -Force
    if ((Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash -ne $ExpectedCpuPatchHashes[$name]) {
        throw "打包暂存区运行库校验失败：$name"
    }
}
Write-Host '已校验 MaaFW 5.16.0 配套依赖，并注入四线程 CPU OCR 关闭自旋补丁。'
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
# 保留 MaaAgentBinary，其中包含 maatouch、minitouch 与 minicap 等设备代理。
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
$HakusanRepairStub = Join-Path $Root 'tools\release\TaskHakusanRepair.stub.json'
$HakusanRepairTarget = Join-Path $TempDir 'assets\resource\base\pipeline\TaskHakusanRepair.json'
if (-not (Test-Path -LiteralPath $HakusanRepairStub)) {
    throw "白山修刀安全壳不存在: $HakusanRepairStub"
}
Copy-Item -LiteralPath $HakusanRepairStub -Destination $HakusanRepairTarget -Force

# 发布包不包含运行时配置和刀帐个人数据；这些文件只存在于开发区的 config/ 中。
if (Test-Path "$TempDir\config") { Remove-Item -Recurse -Force "$TempDir\config" }
if (Test-Path "$TempDir\assets\config") { Remove-Item -Recurse -Force "$TempDir\assets\config" }
if (Test-Path "$TempDir\assets\resource\config") { Remove-Item -Recurse -Force "$TempDir\assets\resource\config" }
if (Test-Path "$TempDir\assets\resource\temp") { Remove-Item -Recurse -Force "$TempDir\assets\resource\temp" }
if (Test-Path "$TempDir\assets\resource\backup") { Remove-Item -Recurse -Force "$TempDir\assets\resource\backup" }
if (Test-Path "$TempDir\assets\resource\base\image\unused") { Remove-Item -Recurse -Force "$TempDir\assets\resource\base\image\unused" }

# 版本更新公告由发布页提供，安装包只保留长期公告。
$AnnouncementDir = Join-Path $TempDir 'assets\resource\announcement'
if (Test-Path -LiteralPath $AnnouncementDir) {
    Get-ChildItem -LiteralPath $AnnouncementDir -File |
        Where-Object { $_.Name -match '^\d+-v.+ 更新公告\.md$' } |
        Remove-Item -Force
}

# Package (compress temp dir contents directly, no wrapper folder)
Compress-Archive -Path "$TempDir\*" -DestinationPath $ZipFile -Force

# Cleanup
Remove-Item -Recurse -Force "$Root\_temp_zip"

Write-Host "打包完成: $ZipFile"
