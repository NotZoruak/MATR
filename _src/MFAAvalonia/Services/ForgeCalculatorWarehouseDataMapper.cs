using MFAAvalonia.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>将已保存的仓库数据映射为限锻计算器的输入值。</summary>
public static class ForgeCalculatorWarehouseDataMapper
{
    /// <summary>读取核心资源、御札和道具；未找到的项目返回零。</summary>
    public static ForgeCalculatorWarehouseValues Read(WarehouseData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return new ForgeCalculatorWarehouseValues(
            GetValue(data.CoreResources, "木炭"),
            GetValue(data.CoreResources, "玉钢"),
            GetValue(data.CoreResources, "冷却材"),
            GetValue(data.CoreResources, "砥石"),
            GetTalismanValue(data.OtherItems, "梅"),
            GetTalismanValue(data.OtherItems, "竹"),
            GetTalismanValue(data.OtherItems, "松"),
            GetTalismanValue(data.OtherItems, "富士"),
            GetValue(data.CoreResources, "委托符"),
            GetValue(data.CoreResources, "加速符"));
    }

    private static int GetTalismanValue(IReadOnlyDictionary<string, int> items, string suffix)
    {
        var exactName = $"御札·{suffix}";
        if (items.TryGetValue(exactName, out var exactValue))
            return Math.Max(0, exactValue);

        var normalizedSuffix = Normalize(suffix);
        return items
            .Where(item => Normalize(item.Key).StartsWith("御札", StringComparison.Ordinal)
                        && Normalize(item.Key).EndsWith(normalizedSuffix, StringComparison.Ordinal))
            .Select(item => Math.Max(0, item.Value))
            .FirstOrDefault();
    }

    private static int GetValue(IReadOnlyDictionary<string, int> values, string key) =>
        values.TryGetValue(key, out var value) ? Math.Max(0, value) : 0;

    private static string Normalize(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Replace("・", "·", StringComparison.Ordinal);
}

/// <summary>限锻计算器从仓库数据读取的输入值。</summary>
public sealed record ForgeCalculatorWarehouseValues(
    int Charcoal,
    int Steel,
    int Coolant,
    int Whetstone,
    int Plum,
    int Bamboo,
    int Pine,
    int Fuji,
    int Permits,
    int Speedups);
