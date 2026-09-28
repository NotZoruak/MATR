$ErrorActionPreference = 'Stop'

function Copy-WithHarvestPrefix($value) {
    if ($null -eq $value) { return $null }
    if ($value -is [string]) {
        return $value.Replace('S_', 'HV_').Replace('[常驻作战]', '[刷收获物]').Replace('常驻作战', '刷收获物')
    }
    if ($value -is [System.Collections.IList]) {
        $items = @()
        foreach ($item in $value) { $items += Copy-WithHarvestPrefix $item }
        return $items
    }
    if ($value -is [pscustomobject]) {
        $result = [ordered]@{}
        foreach ($property in $value.PSObject.Properties) {
            $result[$property.Name] = Copy-WithHarvestPrefix $property.Value
        }
        return [pscustomobject]$result
    }
    return $value
}

function Add-Property($object, [string]$name, $value) {
    $object | Add-Member -MemberType NoteProperty -Name $name -Value $value -Force
}

$sortiePath = Join-Path $PSScriptRoot '..\..\..\assets\resource\base\pipeline\Sortie.json'
$harvestPath = Join-Path $PSScriptRoot '..\..\..\assets\resource\base\pipeline\Harvest.json'
$interfacePath = Join-Path $PSScriptRoot '..\..\..\assets\interface.json'

$sortie = Get-Content -LiteralPath $sortiePath -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 100
$baseNodes = [ordered]@{}
foreach ($property in $sortie.PSObject.Properties) {
    $name = if ($property.Name -eq 'Sortie') { 'Harvest' } else { $property.Name.Replace('S_', 'HV_') }
    $baseNodes[$name] = Copy-WithHarvestPrefix $property.Value
}

$eraTargets = @(
    @(246, 389, '一'),
    @(345, 516, '二'),
    @(447, 379, '三'),
    @(546, 511, '四'),
    @(644, 375, '五'),
    @(750, 508, '六'),
    @(849, 378, '七'),
    @(956, 519, '八')
)
$regionTargets = @(
    @(191, 308, 62, 3),
    @(495, 307, 42, 3),
    @(804, 307, 53, 3),
    @(984, 307, 48, 3)
)
$regionRois = @(
    @(143, 462, 217, 103),
    @(441, 461, 218, 104),
    @(740, 461, 218, 104),
    @(1038, 461, 218, 104)
)

$harvestNodes = [ordered]@{}
$harvestNodes['Harvest'] = [ordered]@{
    anchor = [ordered]@{
        HV_Era1Active = 'HV_Era1_Enter'
        HV_Era2Active = 'HV_Era2_Enter'
        HV_Era3Active = 'HV_Era3_Enter'
        HV_Era4Active = 'HV_Era4_Enter'
        HV_Era5Active = 'HV_Era5_Enter'
        HV_Era6Active = 'HV_Era6_Enter'
        HV_Era7Active = 'HV_Era7_Enter'
        HV_Era8Active = 'HV_Era8_Enter'
    }
    next = @('HV_DetectWhereAmI')
}
$harvestNodes['HV_EraRouter'] = [ordered]@{
    next = @(
        '[Anchor]HV_Era1Active', '[Anchor]HV_Era2Active', '[Anchor]HV_Era3Active', '[Anchor]HV_Era4Active',
        '[Anchor]HV_Era5Active', '[Anchor]HV_Era6Active', '[Anchor]HV_Era7Active', '[Anchor]HV_Era8Active',
        'HV_AllDone'
    )
}
$baseNodes['HV_IsPastEraSelect'].next = @('HV_EraRouter')

for ($era = 1; $era -le 8; $era++) {
    $target = $eraTargets[$era - 1]
    $expected = $target[2]
    $checkNames = @()
    for ($region = 1; $region -le 4; $region++) {
        $checkNames += "HV_Era${era}_Region${region}_Check"
    }

    $harvestNodes["HV_Era${era}_Enter"] = [ordered]@{
        action = [ordered]@{
            type = 'Click'
            param = [ordered]@{ target = @($target[0], $target[1], 1, 1) }
        }
        post_delay = 200
        next = @("HV_Era${era}_Verify", 'HV_DetectWhereAmI')
    }
    $harvestNodes["HV_Era${era}_Verify"] = [ordered]@{
        recognition = [ordered]@{
            type = 'OCR'
            param = [ordered]@{ roi = @(831, 80, 26, 26); expected = @($expected) }
        }
        next = @("HV_Era${era}_IsRegionSelect", 'HV_DetectWhereAmI')
    }
    $harvestNodes["HV_Era${era}_IsRegionSelect"] = [ordered]@{
        recognition = [ordered]@{
            type = 'TemplateMatch'
            param = [ordered]@{
                roi = @(833, 617, 152, 41)
                template = 'Common/地域选择.png'
                green_mask = $true
                threshold = @(0.98)
            }
        }
        next = $checkNames + @("HV_Era${era}_Done")
    }
    for ($region = 1; $region -le 4; $region++) {
        $roi = $regionRois[$region - 1]
        $click = $regionTargets[$region - 1]
        $harvestNodes["HV_Era${era}_Region${region}_Check"] = [ordered]@{
            recognition = [ordered]@{
                type = 'TemplateMatch'
                param = [ordered]@{
                    roi = $roi
                    template = 'Common/收获物_天竺牡丹.png'
                    green_mask = $true
                    threshold = @(0.98)
                }
            }
            action = [ordered]@{
                type = 'Click'
                param = [ordered]@{ target = $click }
            }
            post_wait_freezes = [ordered]@{ time = 100; target = $click }
            next = @('HV_IsTeamSelect', 'HV_DetectWhereAmI')
        }
    }
    $harvestNodes["HV_Era${era}_Done"] = [ordered]@{
        anchor = [ordered]@{ "HV_Era${era}Active" = '' }
        action = [ordered]@{
            type = 'Click'
            param = [ordered]@{ target = @(127, 77, 26, 28) }
        }
        post_wait_freezes = [ordered]@{ time = 100; target = @(127, 77, 26, 28) }
        next = @('HV_DetectWhereAmI')
    }
}
$harvestNodes['HV_AllDone'] = [ordered]@{
    focus = [ordered]@{
        'Node.Action.Succeeded' = [ordered]@{ content = '[刷收获物] 完成'; display = 'file' }
    }
}

foreach ($property in $baseNodes.GetEnumerator()) {
    if ($property.Key -eq 'Harvest') { continue }
    $node = $property.Value
    if ($node.PSObject.Properties.Name -contains 'anchor' -and $node.anchor.PSObject.Properties.Name -contains 'HV_RoundDone') {
        $node.anchor.PSObject.Properties.Remove('HV_RoundDone')
    }
    $harvestNodes[$property.Key] = $node
}
$harvestNodes['HV_CheckHomeBrightness'].next = @('HV_IsHome', 'HV_DetectWhereAmI')
$harvestNodes['HV_SortieSuccess'].PSObject.Properties.Remove('anchor')
$harvestNodes['HV_SortieSuccess'].focus.'Node.Action.Succeeded'.content = '[刷收获物] 出阵'

$harvestNodes | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $harvestPath -Encoding UTF8
