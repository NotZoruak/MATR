using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Helper;
using MFAAvalonia.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MFAAvalonia.ViewModels.UsersControls;

/// <summary>编辑编队预设：目标部队、1-6 号位（刀剑 / 刀装 / 马匹）。名称在任务设置页行内修改，不在此编辑</summary>
public partial class FormationEditorViewModel : ViewModelBase
{
    /// <summary>目标部队下拉选中索引（0-4 对应部队一至五）</summary>
    [ObservableProperty] private int _teamIndex;

    /// <summary>编成前卸下现有装备开关</summary>
    [ObservableProperty] private bool _clearEquipmentBeforeFormation;

    /// <summary>编成后保存游戏部队记录开关</summary>
    [ObservableProperty] private bool _saveGameFormationRecordAfterFormation;

    /// <summary>是否仅使用部队记录</summary>
    [ObservableProperty] private bool _useGameFormationRecordOnly;

    /// <summary>是否仅记录编队</summary>
    [ObservableProperty] private bool _saveGameFormationRecordOnly;

    /// <summary>部队下拉选项（1-5）</summary>
    public string[] TeamOptions { get; } = ["部队一", "部队二", "部队三", "部队四", "部队五"];

    /// <summary>1-6 号位编辑行</summary>
    public ObservableCollection<FormationSlotEdit> Slots { get; } = [];

    /// <summary>从剪影模板文件名读取的刀剑名称，用于可搜索下拉框</summary>
    public string[] SwordOptions { get; } = FormationOptions.LoadSwordOptions();

    private readonly FormationPreset _preset;

    /// <summary>保存回调：preset 参数非 null 表示保存，null 表示取消关闭</summary>
    private readonly Action<FormationPreset?>? _onDone;

    public FormationEditorViewModel(FormationPreset preset, Action<FormationPreset?>? onDone)
    {
        _preset = preset;
        _onDone = onDone;
        _teamIndex = Math.Clamp(preset.Team - 1, 0, 4);
        _clearEquipmentBeforeFormation = preset.ClearEquipmentBeforeFormation;
        _saveGameFormationRecordAfterFormation = preset.SaveGameFormationRecordAfterFormation;
        _useGameFormationRecordOnly = preset.UseGameFormationRecordOnly;
        _saveGameFormationRecordOnly = preset.SaveGameFormationRecordOnly && !_useGameFormationRecordOnly;
        preset.EnsureSlots();
        for (var i = 0; i < 6; i++)
        {
            Slots.Add(new FormationSlotEdit(i + 1, preset.Slots[i]));
        }
    }

    [RelayCommand]
    private void Save()
    {
        // 名称不在此编辑，保持原值
        _preset.Team = TeamIndex + 1;
        _preset.ClearEquipmentBeforeFormation = ClearEquipmentBeforeFormation;
        _preset.SaveGameFormationRecordAfterFormation = SaveGameFormationRecordAfterFormation;
        FormationPreset.SetRecordMode(_preset, UseGameFormationRecordOnly, SaveGameFormationRecordOnly);
        _preset.EnsureSlots();
        for (var i = 0; i < Slots.Count && i < 6; i++)
        {
            _preset.Slots[i].Sword = Slots[i].Sword.Trim();
            _preset.Slots[i].Treasure = Slots[i].Treasure?.Trim() ?? "";
            _preset.Slots[i].Equip = Slots[i].EquipText;
            _preset.Slots[i].Horse = string.IsNullOrEmpty(Slots[i].Horse) ? "无" : Slots[i].Horse;
        }
        _onDone?.Invoke(_preset);
    }

    [RelayCommand]
    private void Cancel()
    {
        _onDone?.Invoke(null);
    }

    partial void OnUseGameFormationRecordOnlyChanged(bool value)
    {
        if (value)
            SaveGameFormationRecordOnly = false;
    }

    partial void OnSaveGameFormationRecordOnlyChanged(bool value)
    {
        if (value)
            UseGameFormationRecordOnly = false;
    }
}

/// <summary>预设编辑界面中的单个位置行（可绑定）</summary>
public partial class FormationSlotEdit : ObservableObject
{
    /// <summary>位置编号 1-6</summary>
    public int Position { get; }

    [ObservableProperty] private string _sword;
    [ObservableProperty] private string _treasure;
    [ObservableProperty] private string _horse;
    [ObservableProperty] private string _equip1;
    [ObservableProperty] private string _equip2;
    [ObservableProperty] private string _equip3;

    /// <summary>马匹下拉选项（含「无」）</summary>
    public string[] HorseOptions => FormationOptions.HorseOptions;

    /// <summary>宝物下拉选项</summary>
    public string[] TreasureOptions => FormationOptions.TreasureOptions;

    /// <summary>刀装下拉选项（含「无」）</summary>
    public string[] EquipOptions => FormationOptions.EquipOptions;

    /// <summary>按旧配置格式拼接三个刀装槽，保持运行期兼容</summary>
    public string EquipText => string.Concat(
        FormationOptions.ToInternalEquipName(Equip1),
        FormationOptions.ToInternalEquipName(Equip2),
        FormationOptions.ToInternalEquipName(Equip3));

    public FormationSlotEdit(int position, FormationSlot slot)
    {
        Position = position;
        _sword = slot.Sword;
        _treasure = string.IsNullOrEmpty(slot.Treasure) ? "无" : slot.Treasure;
        _horse = slot.Horse;
        var equips = FormationOptions.ParseEquipSlots(slot.Equip);
        _equip1 = equips[0];
        _equip2 = equips[1];
        _equip3 = equips[2];
    }
}

/// <summary>编队编辑器使用的候选列表</summary>
public static class FormationOptions
{
    public static readonly string[] HorseOptions = ["无", "王庭", "三国黑", "松风", "小云雀", "高楯黑", "花柑子", "青海波", "望月", "白毛", "鹿毛", "青毛"];

    public static readonly string[] TreasureOptions = ["无", "曜变天目", "狮子螺钿鞍", "南蛮胴具足", "锷・月下梅树透图", "锷・双鹤图", "三所物・菊", "三所物・狮子"];

    public static readonly string[] EquipOptions = ["无", "轻步兵", "重步兵", "精锐兵", "轻骑兵", "重骑兵", "投石兵", "铳兵", "弓兵", "枪兵", "盾兵", "乐器兵", "水炮兵"];

    private static readonly Dictionary<string, string> TreasureOcrNameMap = new(StringComparer.Ordinal)
    {
        ["曜变天目"] = "变天目",
        ["狮子螺钿鞍"] = "狮子螺",
        ["南蛮胴具足"] = "南蛮",
        ["锷・月下梅树透图"] = "月下",
        ["锷・双鹤图"] = "双鹤",
        ["三所物・菊"] = "三所物菊",
        ["三所物・狮子"] = "三所物狮",
    };

    /// <summary>将宝物显示名称转换为运行期 OCR 使用的短词。</summary>
    public static string ToOcrTreasureName(string? displayName)
        => string.IsNullOrEmpty(displayName) || displayName == "无"
            ? ""
            : TreasureOcrNameMap.TryGetValue(displayName, out var ocrName) ? ocrName : displayName;

    private static readonly Dictionary<string, string> EquipNameMap = new(StringComparer.Ordinal)
    {
        ["轻步兵"] = "轻步",
        ["重步兵"] = "重步",
        ["精锐兵"] = "精锐",
        ["轻骑兵"] = "轻骑",
        ["重骑兵"] = "重骑",
        ["投石兵"] = "投石",
        ["铳兵"] = "铳",
        ["弓兵"] = "弓",
        ["枪兵"] = "枪",
        ["盾兵"] = "盾",
        ["乐器兵"] = "乐器",
        ["水炮兵"] = "水炮",
    };

    private static readonly string[] InternalEquipNames = ["轻步", "重步", "精锐", "轻骑", "重骑", "投石", "乐器", "水炮", "铳", "弓", "枪", "盾"];

    /// <summary>将旧配置中的连续刀装名称拆分为三个显示槽位</summary>
    public static string[] ParseEquipSlots(string? text)
    {
        var result = new[] { "无", "无", "无" };
        var remaining = text?.Replace(" ", "") ?? "";
        for (var i = 0; i < result.Length && remaining.Length > 0; i++)
        {
            var internalName = InternalEquipNames.FirstOrDefault(remaining.StartsWith);
            if (internalName == null)
                break;

            result[i] = EquipNameMap.First(pair => pair.Value == internalName).Key;
            remaining = remaining[internalName.Length..];
        }

        return result;
    }

    /// <summary>将界面显示名称转换为现有运行期使用的刀装名称</summary>
    public static string ToInternalEquipName(string? displayName)
        => string.IsNullOrEmpty(displayName) || displayName == "无"
            ? ""
            : EquipNameMap.TryGetValue(displayName, out var internalName) ? internalName : displayName;

    /// <summary>从刀帐目录读取并排序全部刀剑名称</summary>
    public static string[] LoadSwordOptions()
    {
        var path = Path.Combine(AppPaths.ResourceDirectory, "base", "SwordBookCatalog.json");
        if (!File.Exists(path))
            return [];

        var catalog = JsonConvert.DeserializeObject<List<SwordBookCatalogItem>>(File.ReadAllText(path));
        return catalog?.Select(item => item.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToArray() ?? [];
    }

    private sealed record SwordBookCatalogItem(string Number, string Type, string Name, bool TypeOnly = false);
}
