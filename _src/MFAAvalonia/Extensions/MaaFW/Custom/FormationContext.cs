using MFAAvalonia.Helper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>自定编队任务运行期固化上下文：启动时读取一次预设并固化，执行途中不重新读取</summary>
public static class FormationContext
{
    /// <summary>目标部队编号 1-5</summary>
    public static int Team = 1;

    /// <summary>编成前卸下现有装备</summary>
    public static bool ClearEquipment;

    /// <summary>编成后保存部队记录</summary>
    public static bool SaveRecord;

    /// <summary>1-6 号位刀剑名（空 = 不配置）</summary>
    public static readonly string[] Swords = new string[6];

    /// <summary>1-6 号位刀装文本（已去除空格）</summary>
    public static readonly string[] Equips = new string[6];

    /// <summary>1-6 号位马匹名（「无」= 不装备）</summary>
    public static readonly string[] Horses = new string[6];

    /// <summary>1-6 号位宝物 OCR 短词（空 = 不装备）</summary>
    public static readonly string[] Treasures = new string[6];

    /// <summary>有刀的位置列表（1-6，升序）</summary>
    public static List<int> MemberSlots = [];

    /// <summary>刀名 → 刀种映射</summary>
    public static Dictionary<string, string>? SwordTypeMap;

    /// <summary>装备流程当前处理的槽位（1-6）</summary>
    public static int CurrentSlot;

    /// <summary>重置运行期上下文</summary>
    public static void Reset()
    {
        Team = 1;
        ClearEquipment = false;
        SaveRecord = false;
        Array.Clear(Swords);
        Array.Clear(Equips);
        Array.Clear(Horses);
        Array.Clear(Treasures);
        MemberSlots = [];
        SwordTypeMap = null;
        CurrentSlot = 0;
    }

    /// <summary>汉字数字转整数（一~六），失败返回 -1</summary>
    public static int ChineseNumToInt(string? text)
    {
        if (string.IsNullOrEmpty(text)) return -1;
        return text.Trim() switch
        {
            "一" => 1,
            "二" => 2,
            "三" => 3,
            "四" => 4,
            "五" => 5,
            "六" => 6,
            _ => -1,
        };
    }

    /// <summary>刀装候选词（不含「兵」），按长度降序用于最长匹配切分</summary>
    private static readonly string[] EquipKeywords = ["轻步", "重步", "精锐", "轻骑", "重骑", "投石", "乐器", "水炮", "铳", "弓", "枪", "盾"];

    /// <summary>切分刀装文本（无分隔连写，最长匹配），返回刀装词序列</summary>
    public static List<string> SplitEquip(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;

        int pos = 0;
        while (pos < text.Length)
        {
            bool matched = false;
            foreach (var kw in EquipKeywords)
            {
                if (text.Substring(pos).StartsWith(kw, StringComparison.Ordinal))
                {
                    result.Add(kw);
                    pos += kw.Length;
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                LoggerHelper.Warning($"[Formation] 刀装文本无法切分: {text}（位置 {pos}）");
                break;
            }
        }
        return result;
    }

    /// <summary>从刀帐目录加载刀名 → 刀种映射表</summary>
    public static Dictionary<string, string> LoadSwordTypeMap()
    {
        var path = Path.Combine(AppPaths.ResourceDirectory, "base", "SwordBookCatalog.json");
        if (!File.Exists(path))
        {
            LoggerHelper.Warning("[Formation] SwordBookCatalog.json 不存在: " + path);
            return [];
        }
        try
        {
            var catalog = JsonConvert.DeserializeObject<List<SwordBookCatalogItem>>(File.ReadAllText(path)) ?? [];
            return catalog
                .Where(item => !string.IsNullOrEmpty(item.Name) && !string.IsNullOrEmpty(item.Type))
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Type, StringComparer.Ordinal);
        }
        catch (Exception e)
        {
            LoggerHelper.Warning($"[Formation] SwordBookCatalog.json 解析失败: {e.Message}");
            return [];
        }
    }

    /// <summary>刀名 → 刀种，未命中返回 null</summary>
    public static string? GetSwordType(string swordName)
    {
        SwordTypeMap ??= LoadSwordTypeMap();
        return SwordTypeMap.TryGetValue(swordName, out var type) ? type : null;
    }

    private sealed record SwordBookCatalogItem(string Number, string Type, string Name, bool TypeOnly = false);
}
