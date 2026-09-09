using MFAAvalonia.Configuration;
using MFAAvalonia.Models;
using MFAAvalonia.ViewModels.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MFAAvalonia.Services;

/// <summary>负责把更新数据任务的识别草稿写入正式配置。</summary>
public static class UpdateDataPersistenceService
{
    /// <summary>仓库正式数据保存完成时触发。</summary>
    public static event Action? WarehouseDataSaved;

    /// <summary>刀帐正式数据保存完成时触发。</summary>
    public static event Action? SwordBookDataSaved;

    /// <summary>通知刀帐页面重新读取已保存状态。</summary>
    public static void NotifySwordBookDataSaved() => SwordBookDataSaved?.Invoke();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>验证并保存仓库识别草稿；失败时保留原有正式数据。</summary>
    public static bool TrySaveWarehouseDraft(string draftPath)
    {
        if (!TryLoadWarehouseData(draftPath, out var warehouseData))
            return false;

        ConfigurationManager.Current.ReloadFromDisk();
        var existingWarehouse = ConfigurationManager.Current.GetValue(
            ConfigurationKeys.WarehouseData,
            new WarehouseData());
        warehouseData.ResourceHistory =
        [
            .. existingWarehouse.ResourceHistory.Select(CloneSnapshot),
            .. warehouseData.ResourceHistory.Select(CloneSnapshot),
        ];
        ConfigurationManager.Current.SetValue(ConfigurationKeys.WarehouseData, warehouseData);
        WarehouseDataSaved?.Invoke();
        return true;
    }

    /// <summary>验证并保存刀帐识别草稿；失败时保留原有正式数据。</summary>
    public static bool TrySaveSwordBookDraft(string draftPath)
    {
        if (!TryLoadSwordBookStates(draftPath, out var states))
            return false;

        ConfigurationManager.Current.ReloadFromDisk();
        var serializerSettings = new JsonSerializerSettings
        {
            DefaultValueHandling = DefaultValueHandling.Include,
        };
        var serializedStates = JArray.FromObject(
            states,
            Newtonsoft.Json.JsonSerializer.Create(serializerSettings));
        ConfigurationManager.Current.SetValue(ConfigurationKeys.SwordBookEntries, serializedStates);
        NotifySwordBookDataSaved();
        return true;
    }

    private static bool TryLoadWarehouseData(string draftPath, out WarehouseData warehouseData)
    {
        warehouseData = new WarehouseData();
        if (!File.Exists(draftPath))
            return false;

        try
        {
            var draft = System.Text.Json.JsonSerializer.Deserialize<WarehouseDraftDocument>(File.ReadAllText(draftPath), JsonOptions);
            if (draft == null)
                return false;

            var coreResources = NormalizeNumberMap(draft.CoreResources);
            var otherItems = WarehouseScanDraftService.NormalizeOtherItems(NormalizeNumberMap(draft.OtherItems));
            var history = NormalizeSnapshots(draft.ResourceHistory);
            if (coreResources.Count == 0 && otherItems.Count == 0 && history.Count == 0)
                return false;

            warehouseData = new WarehouseData
            {
                CoreResources = coreResources,
                OtherItems = otherItems,
                ResourceHistory = history,
            };
            return true;
        }
        catch
        {
            warehouseData = new WarehouseData();
            return false;
        }
    }

    private static bool TryLoadSwordBookStates(string draftPath, out List<SwordBookPortraitState> states)
    {
        states = [];
        if (!File.Exists(draftPath))
            return false;

        try
        {
            var draftStates = JsonConvert.DeserializeObject<List<SwordBookPortraitState>>(File.ReadAllText(draftPath)) ?? [];
            if (draftStates == null || draftStates.Count == 0)
                return false;

            var numbers = new HashSet<string>(StringComparer.Ordinal);
            var normalizedStates = new List<SwordBookPortraitState>(draftStates.Count);
            foreach (var state in draftStates)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.Number))
                    return false;

                var number = state.Number.Trim();
                if (!numbers.Add(number))
                    return false;

                normalizedStates.Add(new SwordBookPortraitState(
                    number,
                    state.Owned,
                    state.Wounded,
                    state.TrueSword,
                    state.InnerCare,
                    state.Casual));
            }

            states = normalizedStates;
            return true;
        }
        catch
        {
            states = [];
            return false;
        }
    }

    private static Dictionary<string, int> NormalizeNumberMap(Dictionary<string, int>? values)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (values == null)
            return result;

        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            result[pair.Key] = Math.Max(0, pair.Value);
        }

        return result;
    }

    private static List<WarehouseResourceSnapshot> NormalizeSnapshots(List<WarehouseDraftSnapshot>? snapshots)
    {
        if (snapshots == null || snapshots.Count == 0)
            return [];

        return [.. snapshots
            .Where(snapshot => snapshot != null)
            .Select(snapshot => new WarehouseResourceSnapshot
            {
                RecordedAt = snapshot!.RecordedAt,
                Values = NormalizeNumberMap(snapshot.Values),
            })
            .Where(snapshot => snapshot.Values.Count > 0)];
    }

    private static WarehouseResourceSnapshot CloneSnapshot(WarehouseResourceSnapshot snapshot) => new()
    {
        RecordedAt = snapshot.RecordedAt,
        Values = new Dictionary<string, int>(snapshot.Values, StringComparer.Ordinal),
    };

    private sealed class WarehouseDraftDocument
    {
        [JsonPropertyName("core_resources")]
        public Dictionary<string, int>? CoreResources { get; set; }

        [JsonPropertyName("other_items")]
        public Dictionary<string, int>? OtherItems { get; set; }

        [JsonPropertyName("resource_history")]
        public List<WarehouseDraftSnapshot>? ResourceHistory { get; set; }
    }

    private sealed class WarehouseDraftSnapshot
    {
        [JsonPropertyName("recorded_at")]
        public DateTime RecordedAt { get; set; }

        [JsonPropertyName("values")]
        public Dictionary<string, int>? Values { get; set; }
    }
}
