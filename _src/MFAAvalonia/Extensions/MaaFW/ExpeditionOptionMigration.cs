using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 将旧全局远征选项迁移到本丸后勤任务设置。
/// </summary>
public static class ExpeditionOptionMigration
{
    /// <summary>将旧版内番耕作加成跳过选项迁移到开关及嵌套条件。</summary>
    public static bool MigrateLegacyNaibanSkipCondition(MaaInterface.MaaInterfaceSelectOption option)
    {
        if (option.Name != "耕作加成满值时跳过内番"
            || option.SubOptions?.Any(subOption => subOption.Name == "内番耕作加成跳过条件") == true
            || option.Index is not int legacyIndex
            || legacyIndex is < 0 or > 4)
        {
            return false;
        }

        if (legacyIndex == 0)
        {
            option.Index = 0;
            return true;
        }

        option.Index = 1;
        option.SubOptions ??= [];
        option.SubOptions.Add(new MaaInterface.MaaInterfaceSelectOption
        {
            Name = "内番耕作加成跳过条件",
            Index = legacyIndex - 1
        });
        return true;
    }

    /// <summary>
    /// 将旧全局长期计划开关与疲劳阈值迁入本丸后勤的远征设置。
    /// </summary>
    public static bool MigrateLegacyLongTermPlan(
        IEnumerable<MaaInterface.MaaInterfaceSelectOption>? legacyOptions,
        MaaInterface.MaaInterfaceTask? logisticsTask,
        bool hasCurrentExpeditionSettings)
    {
        if (hasCurrentExpeditionSettings || logisticsTask?.Entry != "Expedition" || logisticsTask.Option == null)
            return false;

        var legacy = legacyOptions?.ToDictionary(option => option.Name ?? string.Empty)
            ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectOption>();
        var expedition = logisticsTask.Option.FirstOrDefault(option => option.Name == "远征");
        if (expedition == null || legacy.Count == 0)
            return false;

        expedition.SubOptions ??= [];
        var migrated = false;

        if (legacy.TryGetValue("长期远征计划", out var legacyLongTermPlan))
        {
            var longTermPlan = expedition.SubOptions.FirstOrDefault(option => option.Name == "长期远征计划");
            if (longTermPlan != null)
            {
                longTermPlan.Index = legacyLongTermPlan.Index;
                if (legacyLongTermPlan.SubOptions?.FirstOrDefault(option => option.Name == "疲劳阈值") is { } legacyThreshold)
                {
                    longTermPlan.SubOptions ??= [];
                    var threshold = longTermPlan.SubOptions.FirstOrDefault(option => option.Name == "疲劳阈值");
                    if (threshold != null)
                    {
                        threshold.Data = legacyThreshold.Data != null
                            ? new Dictionary<string, string?>(legacyThreshold.Data)
                            : null;
                    }
                    else
                    {
                        longTermPlan.SubOptions.Add(CloneOption(legacyThreshold));
                    }
                }

                migrated = true;
            }
        }

        return migrated;
    }

    private static MaaInterface.MaaInterfaceSelectOption CloneOption(MaaInterface.MaaInterfaceSelectOption source) => new()
    {
        Name = source.Name,
        Index = source.Index,
        Data = source.Data != null ? new Dictionary<string, string?>(source.Data) : null,
        SelectedCases = source.SelectedCases != null ? new List<string>(source.SelectedCases) : null,
        SubOptions = source.SubOptions?.Select(CloneOption).ToList(),
    };
}
