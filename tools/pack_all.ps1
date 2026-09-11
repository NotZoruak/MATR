# 一键打包 win-x64 与 macos-arm64 两个发布包（本地手动发布用）
# 用法: pwsh tools/pack_all.ps1 -Version v0.14.0-beta.3
# 产物: MATR-$Version-win-x64.zip 与 MATR-$Version-macos-arm64.zip（仓库根目录）
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Project = Join-Path $Root "_src\MFAAvalonia.Desktop\MFAAvalonia.Desktop.csproj"
$PublishBase = Join-Path $Root "_src\bin\AnyCPU\Release"

function Invoke-Dotnet {
    param([string[]]$Arguments)
    dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet 命令失败（退出码 $LASTEXITCODE）: dotnet $($Arguments -join ' ')" }
}

Write-Host "== Windows x64 发布 =="
Invoke-Dotnet @('publish', $Project, '-c', 'Release', '-p:Platform=AnyCPU', '-r', 'win-x64',
    "-p:PublishDir=$(Join-Path $PublishBase 'win-x64\publish')", '--self-contained', 'false')
pwsh (Join-Path $Root "tools\pack_win.ps1") -Version $Version -PublishDir (Join-Path $PublishBase 'win-x64\publish')

Write-Host "== macOS arm64 交叉发布 =="
if (Test-Path (Join-Path $PublishBase 'osx-arm64\publish')) {
    Remove-Item -LiteralPath (Join-Path $PublishBase 'osx-arm64\publish') -Recurse -Force
}
Invoke-Dotnet @('restore', $Project, '-p:Configuration=Release', '-p:Platform=AnyCPU', '-r', 'osx-arm64',
    '-p:MATR_TARGET_RID=osx-arm64', '--disable-parallel')
Invoke-Dotnet @('publish', $Project, '-c', 'Release', '-p:Platform=AnyCPU', '-r', 'osx-arm64',
    '-p:MATR_TARGET_RID=osx-arm64',
    "-p:PublishDir=$(Join-Path $PublishBase 'osx-arm64\publish')",
    '--self-contained', 'true', '--no-restore')
pwsh (Join-Path $Root "tools\pack_mac.ps1") -Version $Version -PublishDir (Join-Path $PublishBase 'osx-arm64\publish')

Write-Host "全部完成:"
Write-Host "  $(Join-Path $Root "MATR-$Version-win-x64.zip")"
Write-Host "  $(Join-Path $Root "MATR-$Version-macos-arm64.zip")"
