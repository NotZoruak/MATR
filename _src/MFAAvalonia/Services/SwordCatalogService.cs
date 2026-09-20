using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>刀剑名册条目。</summary>
public sealed record SwordCatalogEntry(string BaseName, string Type, string DisplayName);

/// <summary>提供刀剑名册加载、搜索与排序逻辑。</summary>
public static class SwordCatalogService
{
    private static readonly string[] TypeOrder = ["短刀", "胁差", "打刀", "太刀", "大太刀", "枪", "薙刀", "剑"];

    private sealed record CatalogRawItem(string Number, string Type, string Name, bool TypeOnly = false);

    /// <summary>从资源目录读取可供设置页面选择的刀剑名册。</summary>
    public static List<SwordCatalogEntry> Load(string catalogPath)
    {
        if (!File.Exists(catalogPath))
            return [];

        var items = (JsonConvert.DeserializeObject<List<CatalogRawItem>>(File.ReadAllText(catalogPath)) ?? [])
            .Where(item => !item.TypeOnly)
            .ToList();
        var duplicateNames = items.GroupBy(item => item.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.Last().Number, StringComparer.Ordinal);

        var catalog = new List<SwordCatalogEntry>();
        foreach (var item in items)
        {
            var displayName = duplicateNames.TryGetValue(item.Name, out var lastNumber) && lastNumber == item.Number
                ? $"{item.Name}·极"
                : item.Name;
            var baseName = displayName.EndsWith("·极", StringComparison.Ordinal) ? displayName[..^2] : displayName;
            if (catalog.All(entry => entry.BaseName != baseName))
                catalog.Add(new SwordCatalogEntry(baseName, item.Type, displayName));
        }

        return catalog;
    }

    /// <summary>按刀剑名称或刀种包含关系筛选；空搜索不返回候选。</summary>
    public static IEnumerable<SwordCatalogEntry> Search(
        IReadOnlyList<SwordCatalogEntry> catalog,
        string? keyword)
    {
        var normalizedKeyword = keyword?.Trim() ?? string.Empty;
        if (normalizedKeyword.Length == 0)
            return [];

        return catalog
            .Select((entry, index) => (entry, index))
            .Where(item => Matches(item.entry.DisplayName, item.entry.Type, normalizedKeyword))
            .OrderBy(item => TypeRank(item.entry.Type))
            .ThenBy(item => item.index)
            .Select(item => item.entry);
    }

    /// <summary>判断搜索词是否命中刀剑名称或刀种。</summary>
    public static bool Matches(string displayName, string type, string? keyword)
    {
        var normalizedKeyword = keyword?.Trim() ?? string.Empty;
        return normalizedKeyword.Length > 0
            && (displayName.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase)
                || type.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按刀帐中的刀种顺序排序，未知刀种排在末尾。</summary>
    public static int TypeRank(string type)
    {
        var index = Array.IndexOf(TypeOrder, type);
        return index < 0 ? TypeOrder.Length : index;
    }
}
