using MFAAvalonia.Models;
using MFAAvalonia.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MFAAvalonia.Services;

/// <summary>处理仓库自动识别产生的临时草稿。</summary>
public static class WarehouseScanDraftService
{
    private static readonly Dictionary<string, string> OtherItemNameCorrections = new(StringComparer.Ordinal)
    {
        ["套纸笔"] = "一套纸笔",
        ["狮子螺钾鞍"] = "狮子螺钿鞍",
        ["口团子"] = "一口团子",
        ["锣·月下梅树透图碎片"] = "锷·月下梅树透图碎片",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>将 OCR 文本解析为资源数量。</summary>
    public static bool TryParseCount(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim()
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("，", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);

        // 数量为 1 时，OCR 偶尔会把数字识别成汉字“一”。
        if (string.Equals(normalized, "一", StringComparison.Ordinal))
        {
            value = 1;
            return true;
        }

        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out value) && value >= 0;
    }

    /// <summary>读取临时草稿；文件不存在或内容损坏时返回空草稿。</summary>
    public static WarehouseData Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new WarehouseData();

            var draft = JsonSerializer.Deserialize<WarehouseScanDraft>(File.ReadAllText(path), JsonOptions);
            return draft?.ToWarehouseData() ?? new WarehouseData();
        }
        catch
        {
            return new WarehouseData();
        }
    }

    /// <summary>更新一个核心资源，并以原子方式写回草稿。</summary>
    public static void UpdateCoreResource(string path, string resource, int value)
    {
        var data = Load(path);
        data.CoreResources[resource] = Math.Max(0, value);
        Save(path, data);
    }

    /// <summary>清空本次自动识别使用的其他物品草稿，避免沿用上一次的旧数量。</summary>
    public static void ClearOtherItems(string path)
    {
        var data = Load(path);
        data.OtherItems.Clear();
        Save(path, data);
    }

    /// <summary>更新一个其他物品，并保留草稿中的其他识别结果。</summary>
    public static void UpdateOtherItem(string path, string item, int value)
    {
        var data = Load(path);
        var normalizedItem = NormalizeOtherItemName(item);
        foreach (var alias in data.OtherItems.Keys
                     .Where(key => !string.Equals(key, normalizedItem, StringComparison.Ordinal)
                                && string.Equals(NormalizeOtherItemName(key), normalizedItem, StringComparison.Ordinal))
                     .ToList())
            data.OtherItems.Remove(alias);
        data.OtherItems.Remove(item);
        if (value > 0)
            data.OtherItems[normalizedItem] = value;
        Save(path, data);
    }

    /// <summary>修正已知的其他物品 OCR 误识别名称。</summary>
    public static string NormalizeOtherItemName(string item)
    {
        item = item
            .Replace("御守桃", "御守·桃", StringComparison.Ordinal)
            .Replace("御守极", "御守·极", StringComparison.Ordinal)
            .Replace("御守・桃", "御守·桃", StringComparison.Ordinal)
            .Replace("御守・极", "御守·极", StringComparison.Ordinal);

        if (item.Contains("月下梅树", StringComparison.Ordinal))
            return "锷·月下梅树透图碎片";

        return OtherItemNameCorrections.TryGetValue(item, out var corrected) ? corrected : item;
    }

    /// <summary>读取当前配置中最近保存的其他物品名称。</summary>
    public static IReadOnlyCollection<string> LoadSavedOtherItemNames()
    {
        var data = ConfigurationManager.Current.GetValue(ConfigurationKeys.WarehouseData, new WarehouseData());
        return NormalizeOtherItems(data.OtherItems).Keys.ToArray();
    }

    /// <summary>按名称归一化并合并其他物品，避免历史 OCR 别名重复显示。</summary>
    public static Dictionary<string, int> NormalizeOtherItems(IEnumerable<KeyValuePair<string, int>> items)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.Value <= 0 || string.IsNullOrWhiteSpace(item.Key))
                continue;

            var normalizedName = NormalizeOtherItemName(item.Key);
            if (result.TryGetValue(normalizedName, out var currentValue))
                result[normalizedName] = Math.Max(currentValue, item.Value);
            else
                result[normalizedName] = item.Value;
        }
        return result;
    }

    /// <summary>优先使用已保存名称匹配 OCR 结果，避免手动校对后的名称再次被 OCR 名称覆盖。</summary>
    public static string ResolveOtherItemName(string item, IEnumerable<string> savedNames)
    {
        var normalizedItem = NormalizeOtherItemName(item);

        // 这两个名称只差一个字，禁止进入模糊匹配，避免御守·桃被错误合并到御守·极。
        if (normalizedItem is "御守·桃" or "御守·极")
            return normalizedItem;

        var candidates = savedNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => (Name: name, Normalized: NormalizeOtherItemName(name)))
            .ToList();

        var exact = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, normalizedItem, StringComparison.Ordinal)
            || string.Equals(candidate.Normalized, normalizedItem, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(exact.Name))
            return exact.Name;

        var closest = candidates
            .Select(candidate => (candidate.Name, Distance: CalculateEditDistance(normalizedItem, candidate.Normalized)))
            .Where(candidate => candidate.Distance <= Math.Max(1, normalizedItem.Length / 4))
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(closest.Name) ? normalizedItem : closest.Name;
    }

    private static int CalculateEditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var replacementCost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + replacementCost);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    /// <summary>在完整识别结束时追加一次核心资源历史快照。</summary>
    public static void AppendSnapshot(string path, IDictionary<string, int> values, DateTime? recordedAt = null)
    {
        var data = Load(path);
        data.ResourceHistory.Add(new WarehouseResourceSnapshot
        {
            RecordedAt = recordedAt ?? DateTime.Now,
            Values = new Dictionary<string, int>(values, StringComparer.Ordinal),
        });
        Save(path, data);
    }

    /// <summary>删除上一次识别留下的临时草稿。</summary>
    public static void Clear(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>保存草稿，避免程序中断时留下半份 JSON。</summary>
    public static void Save(string path, WarehouseData data)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        var draft = WarehouseScanDraft.FromWarehouseData(data);
        File.WriteAllText(tempPath, JsonSerializer.Serialize(draft, JsonOptions));
        File.Move(tempPath, path, true);
    }

    private sealed class WarehouseScanDraft
    {
        [JsonPropertyName("core_resources")]
        public Dictionary<string, int> CoreResources { get; set; } = new(StringComparer.Ordinal);

        [JsonPropertyName("other_items")]
        public Dictionary<string, int> OtherItems { get; set; } = new(StringComparer.Ordinal);

        [JsonPropertyName("resource_history")]
        public List<WarehouseResourceSnapshot> ResourceHistory { get; set; } = [];

        public static WarehouseScanDraft FromWarehouseData(WarehouseData data) => new()
        {
            CoreResources = new Dictionary<string, int>(data.CoreResources, StringComparer.Ordinal),
            OtherItems = new Dictionary<string, int>(data.OtherItems, StringComparer.Ordinal),
            ResourceHistory = [.. data.ResourceHistory.Select(snapshot => new WarehouseResourceSnapshot
            {
                RecordedAt = snapshot.RecordedAt,
                Values = new Dictionary<string, int>(snapshot.Values, StringComparer.Ordinal),
            })],
        };

        public WarehouseData ToWarehouseData() => new()
        {
            CoreResources = new Dictionary<string, int>(CoreResources ?? [], StringComparer.Ordinal),
            OtherItems = new Dictionary<string, int>(OtherItems ?? [], StringComparer.Ordinal),
            ResourceHistory = [.. (ResourceHistory ?? []).Select(snapshot => new WarehouseResourceSnapshot
            {
                RecordedAt = snapshot.RecordedAt,
                Values = new Dictionary<string, int>(snapshot.Values, StringComparer.Ordinal),
            })],
        };
    }
}
