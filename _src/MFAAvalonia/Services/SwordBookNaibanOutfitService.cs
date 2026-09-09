using MFAAvalonia.Configuration;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>将内番服识别结果写入已保存的刀帐状态。</summary>
public static class SwordBookNaibanOutfitService
{
    /// <summary>从已保存刀帐中选出至多两把已拥有但尚未拥有内番服的刀剑。</summary>
    public static IReadOnlyList<NaibanOutfitTarget> SelectMissingOutfitTargets(string catalogPath)
    {
        if (!TryLoadCatalog(catalogPath, out var catalog))
            return [];

        var states = ConfigurationManager.Current.GetValue<List<SwordBookPortraitState>>(
            ConfigurationKeys.SwordBookEntries,
            []);
        if (states.Count == 0)
            return [];

        return (from catalogItem in catalog
                join state in states on catalogItem.Number equals state.Number
                where state.Owned
                group new { catalogItem, state } by catalogItem.Name into nameGroup
                let selected = nameGroup
                    .OrderByDescending(item => ParseNumber(item.catalogItem.Number))
                    .First()
                where !selected.state.InnerCare
                orderby ParseNumber(selected.catalogItem.Number)
                select new NaibanOutfitTarget(selected.catalogItem.Name, selected.catalogItem.Type))
            .Take(2)
            .ToList();
    }

    /// <summary>
    /// 为已拥有的同名刀剑中序号最大的条目勾选内番服；没有已拥有条目时为最小序号登记拥有和内番服。
    /// </summary>
    public static IReadOnlyList<string> MarkOwnedOutfits(IEnumerable<string> swordNames, string catalogPath)
    {
        var names = swordNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (names.Count == 0 || !TryLoadCatalog(catalogPath, out var catalog))
            return [];

        var states = ConfigurationManager.Current.GetValue<List<SwordBookPortraitState>>(
            ConfigurationKeys.SwordBookEntries,
            []);
        if (states.Count == 0)
            return [];

        var targetNumbers = new HashSet<string>(StringComparer.Ordinal);
        var synchronizedNames = new List<string>();
        foreach (var name in names)
        {
            var candidates = (from catalogItem in catalog
                              join state in states on catalogItem.Number equals state.Number
                              where catalogItem.Name == name
                              orderby ParseNumber(catalogItem.Number) descending
                              select state).ToList();
            var target = candidates.FirstOrDefault(state => state.Owned)
                ?? candidates.OrderBy(state => ParseNumber(state.Number)).FirstOrDefault();
            if (target == null)
                continue;

            targetNumbers.Add(target.Number);
            synchronizedNames.Add(name);
        }

        if (targetNumbers.Count == 0)
            return [];

        var updated = states
            .Select(state => targetNumbers.Contains(state.Number) && (!state.Owned || !state.InnerCare)
                ? state with { Owned = true, InnerCare = true }
                : state)
            .ToList();
        if (updated.SequenceEqual(states))
            return synchronizedNames;

        ConfigurationManager.Current.SetValue(ConfigurationKeys.SwordBookEntries, updated);
        UpdateDataPersistenceService.NotifySwordBookDataSaved();
        return synchronizedNames;
    }

    private static bool TryLoadCatalog(string catalogPath, out List<SwordBookCatalogItem> catalog)
    {
        catalog = [];
        if (!File.Exists(catalogPath))
            return false;

        try
        {
            catalog = JsonConvert.DeserializeObject<List<SwordBookCatalogItem>>(File.ReadAllText(catalogPath))
                ?.Where(item => !item.TypeOnly && !string.IsNullOrWhiteSpace(item.Number) && !string.IsNullOrWhiteSpace(item.Name))
                .ToList()
                ?? [];
            return catalog.Count > 0;
        }
        catch
        {
            catalog = [];
            return false;
        }
    }

    private static int ParseNumber(string number) => int.TryParse(number, out var value) ? value : int.MinValue;

    private sealed record SwordBookCatalogItem(string Number, string Type, string Name, bool TypeOnly = false);
}

/// <summary>自动内番需要优先安排的刀剑及其刀种。</summary>
public sealed record NaibanOutfitTarget(string Name, string Type);
