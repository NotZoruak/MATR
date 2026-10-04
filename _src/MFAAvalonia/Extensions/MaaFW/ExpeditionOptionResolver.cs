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
}
