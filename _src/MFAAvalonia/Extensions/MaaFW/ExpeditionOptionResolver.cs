using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Extensions.MaaFW;

/// <summary>
/// 解析本丸后勤任务中的远征设置。
/// </summary>
public static class ExpeditionOptionResolver
{
    /// <summary>
    /// 获取当前实例任务列表中的后勤任务。
    /// </summary>
    public static IEnumerable<MaaInterface.MaaInterfaceTask> GetCurrentTasks()
    {
        if (!MaaProcessorManager.IsInstanceCreated)
            return [];

        return MaaProcessorManager.Instance.Current.ViewModel?.TaskItemViewModels
            .Select(item => item.InterfaceItem)
            .OfType<MaaInterface.MaaInterfaceTask>()
            .ToList() ?? [];
    }

    /// <summary>
    /// 仅在远征和长期远征计划均开启时返回长期计划选项。
    /// </summary>
    public static MaaInterface.MaaInterfaceSelectOption? FindEnabledLongTermPlanOption(
        IEnumerable<MaaInterface.MaaInterfaceTask> tasks)
    {
        var logistics = tasks.FirstOrDefault(task => task.Name == "后勤" && task.Entry == "Expedition");
        var expedition = logistics?.Option?.FirstOrDefault(option => option.Name == "远征");
        if (expedition?.Index != 0)
            return null;

        var plan = expedition.SubOptions?.FirstOrDefault(option => option.Name == "长期远征计划");
        return plan?.Index == 1 ? plan : null;
    }

    /// <summary>
    /// 获取同步后勤需要读取的远征选项名称。新配置从「远征」上级选项进入，
    /// 由常规 option 合并递归处理队伍地图及长期计划；旧配置仍按顶层队伍选项处理。
    /// </summary>
    public static List<string> GetSyncOptionNames(
        MaaInterface.MaaInterfaceTask? logisticsTask,
        List<string> legacyTeamOptionNames)
    {
        if (logisticsTask?.Option?.Any(option => option.Name == "远征") == true)
            return ["远征"];

        return legacyTeamOptionNames;
    }

    /// <summary>获取当前启用的远征组中的设置；兼容旧版顶层队伍选项。</summary>
    public static List<MaaInterface.MaaInterfaceSelectOption> GetExpeditionOptionsForSync(
        MaaInterface.MaaInterfaceTask? logisticsTask)
    {
        var options = logisticsTask?.Option;
        if (options == null)
            return [];

        var expedition = options.FirstOrDefault(option => option.Name == "远征");
        if (expedition == null)
            return options;

        return expedition.Index == 0 ? expedition.SubOptions ?? [] : [];
    }

    /// <summary>读取指定部队当前选择的远征地图序号；0 表示休息。</summary>
    public static MaaInterface.MaaInterfaceSelectOption? FindTeamMapOption(
        MaaInterface.MaaInterfaceTask? logisticsTask,
        string teamOptionName)
        => GetExpeditionOptionsForSync(logisticsTask)
            .FirstOrDefault(option => option.Name == teamOptionName);
}
