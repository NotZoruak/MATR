using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 管理任务专属跳过位置的配置迁移与读取。
/// </summary>
public static class CaptainSettingsHelper
{
    /// <summary>
    /// 将旧全局跳过位置迁移到尚未保存任务专属值的任务。
    /// </summary>
    public static int MigrateLegacySkipPositions(
        IEnumerable<MaaInterface.MaaInterfaceTask> tasks,
        IEnumerable<MaaInterface.MaaInterfaceSelectOption> globalOptions)
    {
        var legacy = globalOptions.FirstOrDefault(option => option.Name == "拖拽跳过位置");
        if (legacy?.SelectedCases == null)
            return 0;

        var migratedCount = 0;
        foreach (var task in tasks)
        {
            var skipOptionName = CaptainSettingsDecision.GetSkipOptionName(task.Entry);
            var captainOptionName = GetCaptainOptionName(task.Entry);
            if (skipOptionName == null || captainOptionName == null)
                continue;

            var captainOption = task.Option?.FirstOrDefault(option => option.Name == captainOptionName);
            if (captainOption == null)
                continue;

            captainOption.SubOptions ??= [];
            if (captainOption.SubOptions.Any(option => option.Name == skipOptionName))
                continue;

            captainOption.SubOptions.Add(new MaaInterface.MaaInterfaceSelectOption
            {
                Name = skipOptionName,
                SelectedCases = [.. legacy.SelectedCases]
            });
            migratedCount++;
        }

        return migratedCount;
    }

    /// <summary>
    /// 读取已启用换队长任务的跳过位置名称。
    /// </summary>
    public static IReadOnlyList<string> GetSelectedSkipPositions(MaaInterface.MaaInterfaceTask? task)
    {
        if (task == null)
            return [];

        var skipOptionName = CaptainSettingsDecision.GetSkipOptionName(task.Entry);
        var captainOptionName = GetCaptainOptionName(task.Entry);
        if (skipOptionName == null || captainOptionName == null)
            return [];

        var captainOption = task.Option?.FirstOrDefault(option => option.Name == captainOptionName);
        if (captainOption?.SelectedCases?.Contains("") != true)
            return [];

        return captainOption.SubOptions?.FirstOrDefault(option => option.Name == skipOptionName)
            ?.SelectedCases?.ToList() ?? [];
    }

    private static string? GetCaptainOptionName(string? entry) => entry switch
    {
        "Sortie" => "S_换队长",
        "Underground" => "U_换队长",
        "LRentaisen" => "LR_换队长",
        "Hanapai" => "HP_换队长",
        "EdoCastle" => "EC_换队长",
        "TacticalTraining" => "TT_换队长",
        _ => null
    };
}
