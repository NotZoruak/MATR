using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Models;

/// <summary>所持刀剑的一振，唯一编号作为稳定身份。</summary>
public sealed class HeldSwordRow
{
    public required string Serial { get; init; }
    public required JObject Data { get; init; }
    public JObject? Definition { get; init; }
    public required string Name { get; init; }
    public string Team { get; init; } = "未编队";
    public sealed record Troop(string Kind, string Current, string Maximum);
    public Troop?[] Troops { get; init; } = new Troop?[3];
    public string Horse { get; init; } = "—";
    public string Charm { get; init; } = "—";
    public long? Number(string key) => long.TryParse(Data[key]?.ToString(), out var value) ? value : null;
    public long? StaticNumber(string key) => long.TryParse(Definition?[key]?.ToString(), out var value) ? value : null;
    public string Protection => Number("protect") switch { 0 => "无锁", 1 => "上锁", 2 => "喜爱", _ => "未知" };
    public string Type => StaticNumber("type") switch { 1 => "短刀", 2 => "胁差", 3 => "打刀", 4 => "太刀", 5 => "大太刀", 6 => "枪", 7 => "薙刀", 8 => "剑", _ => "未知" };
    public long? NeededExperience { get; init; }
    public string LevelTip => $"等级：{Text(Number("level"))}\n升级还需：{Text(NeededExperience)}\n累计经验：{Text(Number("exp"))}";
    public string NameTip => $"刀名：{Name}\n刀帐号：{Text(Number("sword_id"))}\n唯一 ID：{Serial}";
    public static string Text(long? value) => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "—";

    public (long Gained, long Total)? Bonus(string key)
    {
        var remaining = Number(key + "_up");
        var total = StaticNumber(key + "_up");
        return remaining.HasValue && total.HasValue && remaining >= 0 && total >= remaining
            ? (total.Value - remaining.Value, total.Value) : null;
    }

    public bool IsMaximum(string key)
    {
        if (Definition == null || Number(key == "hp" ? "hp_max" : key) == null) return false;
        if (key == "loyalties") return Number(key) == StaticNumber(key);
        return Number(key + "_up") == 0;
    }

    public object? SortingValue(string key) => SortValue(key, true);

    public object? SortValue(string key, bool experience) => key switch
    {
        "Protect" => Number("protect"), "Team" => Team, "Name" => Name,
        "Book" => Number("sword_id"), "Serial" => long.TryParse(Serial, out var id) ? id : null,
        "Type" => Type, "Level" => Number(experience ? "exp" : "level"),
        "Ranbu" => Number("ranbu_level"), "Health" => Number("hp"), "Fatigue" => Number("fatigue"),
        "hp" => Number("hp_max"), "Troops" => string.Join("/", Troops.Select(x => x?.Kind)), "Horse" => Horse, "Charm" => Charm, "Appeared" => Data["created_at"]?.ToString(),
        _ => Number(key)
    };
}
