$ErrorActionPreference = 'Stop'

$path = Join-Path $PSScriptRoot '..\..\..\assets\interface.json'
$text = Get-Content -LiteralPath $path -Raw -Encoding UTF8
$root = [System.Text.Json.Nodes.JsonNode]::Parse($text).AsObject()
$options = $root['option'].AsObject()
$jsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$jsonOptions.WriteIndented = $true

$names = @(
    'S_选择部队', 'S_选择阵形', 'S_重伤处理', 'S_疲劳处理', 'S_补充刀装',
    'S_刀装保护', 'S_疲劳撤退', 'S_自动行军', 'S_换队长', 'S_道中撤退', 'S_避战检非'
)
$blocks = @()
foreach ($name in $names) {
    $target = $name.Replace('S_', 'HV_')
    if ($options[$target] -ne $null) { continue }
    $cloneText = $options[$name].ToJsonString($jsonOptions).Replace('S_', 'HV_')
    $cloneText = $cloneText.Replace('常驻作战', '刷收获物')
    $cloneText = $cloneText -replace ('"name": "' + [regex]::Escape($name) + '"'), ('"name": "' + $target + '"')
    $blocks += "    `"$target`": $cloneText"
}

$marker = '    "RB_疲劳处理": {'
if ($blocks.Count -gt 0) {
    $insertion = ($blocks -join ",`r`n") + ",`r`n"
    $text = $text.Replace($marker, $insertion + $marker)
}

$bossStart = $text.IndexOf('    "HV_不进王点": {', [System.StringComparison]::Ordinal)
$bossEnd = $text.IndexOf('    "HV_道中撤退": {', $bossStart, [System.StringComparison]::Ordinal)
if ($bossStart -ge 0 -and $bossEnd -gt $bossStart) {
    $before = $text.Substring(0, $bossStart)
    $boss = $text.Substring($bossStart, $bossEnd - $bossStart).Replace('S_', 'HV_')
    $after = $text.Substring($bossEnd)
    $text = $before + $boss + $after
}

Set-Content -LiteralPath $path -Value $text -Encoding UTF8
