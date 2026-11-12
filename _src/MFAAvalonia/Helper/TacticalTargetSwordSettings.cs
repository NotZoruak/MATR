using MFAAvalonia.Extensions.MaaFW;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Helper;

/// <summary>战术强化任务内的目标刀剑名单，不读写全局掉落播报配置。</summary>
public static class TacticalTargetSwordSettings
{
    public const string FilterOptionName = "TT_筛选目标刀剑";
    public const string ListOptionName = "TT_目标刀剑";
    public const string DataKey = "swords";

    public static IReadOnlyList<string> DefaultNames { get; } = Array.AsReadOnly(new[]
    {
        "三日月宗近", "小狐丸", "大典太光世", "骚速剑", "数珠丸恒次", "大包平",
        "明石国行", "千子村正", "大般若长光", "小龙景光", "不动行光", "小乌丸",
        "笼手切江", "谦信景光", "小豆长光", "日向正宗", "静形薙刀", "南泉一文字",
        "千代金丸", "南海太郎朝尊", "肥前忠广", "桑名江", "松井江", "泛尘"
    });

    /// <summary>仅尚无保存数据时使用默认值，空名单或损坏的数据均不自动恢复。</summary>
    public static IReadOnlyList<string> ReadNames(MaaInterface.MaaInterfaceSelectOption? option)
    {
        if (option?.Data == null || !option.Data.TryGetValue(DataKey, out var saved))
            return DefaultNames;
        try
        {
            return (JsonConvert.DeserializeObject<List<string>>(saved ?? "[]") ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
                .Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void SaveNames(MaaInterface.MaaInterfaceSelectOption option, IEnumerable<string> names)
    {
        option.Data ??= new();
        option.Data[DataKey] = JsonConvert.SerializeObject(names.Distinct(StringComparer.Ordinal).ToArray());
    }
}
