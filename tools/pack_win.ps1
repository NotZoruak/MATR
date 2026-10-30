param(
[string]$Version = "v0.15.5-beta.1",
    [string]$PublishDir = ""
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$TempBase = "$Root\_temp_zip"
$TempDir = "$TempBase\MATR"
$Platform = "win-x64"
$ZipFile = "$Root\MATR-$Version-$Platform.zip"
$SourceDir = if ([string]::IsNullOrWhiteSpace($PublishDir)) { $Root } else { (Resolve-Path $PublishDir).Path }
$CpuPatchRuntime = Join-Path $Root 'runtimes\win-x64\native'
$ExpectedCpuPatchHashes = [ordered]@{
    'MaaFramework.dll' = '1018F1719EEAC20657BB8AC3BB357374DE9C33678DC0D9DF182B18DB513821E1'
    'fastdeploy_ppocr_maa.dll' = 'A7D12E9B0E7BB6257A67B56051823502DCB474A40E5911D4CBBBDD50F0606085'
    'onnxruntime_maa.dll' = '56E9D7D3BEAAD24E2CE79885201FE19F1B4DBDF511CF1D4BCB03549999C604DC'
    'opencv_world4_maa.dll' = '132C00F39772159EC6F3586E662F9996F725429F72AE42AF603B97AA80ACACEB'
}
foreach ($name in $ExpectedCpuPatchHashes.Keys) {
    $path = Join-Path $CpuPatchRuntime $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "缺少 MaaFW 5.14.3 CPU OCR 补丁运行库：$path。请按 tools/patches 中的说明构建并放置。"
    }
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actualHash -ne $ExpectedCpuPatchHashes[$name]) {
        throw "MaaFW 5.14.3 补丁依赖校验失败：$name；实际摘要：$actualHash"
    }
}

$SourceFramework = Join-Path $SourceDir 'runtimes\win-x64\native\MaaFramework.dll'
if (-not (Test-Path -LiteralPath $SourceFramework -PathType Leaf)) {
    throw "发布目录缺少 MaaFramework.dll：$SourceFramework"
}
$SourceFrameworkHash = (Get-FileHash -LiteralPath $SourceFramework -Algorithm SHA256).Hash
if ($SourceFrameworkHash -notin @('7085FEDCAAE06D7521020EDFE8C3A3F9545986877E1B4A4BB8CF0188B39F97BE', $ExpectedCpuPatchHashes['MaaFramework.dll'])) {
    throw '发布目录不是预期的官方 5.14.3 或当前双线程补丁构建，拒绝混入其他版本控制器。'
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
Write-Host '已校验 MaaFW 5.14.3 配套依赖，并注入双线程 CPU OCR 关闭自旋补丁。'
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
