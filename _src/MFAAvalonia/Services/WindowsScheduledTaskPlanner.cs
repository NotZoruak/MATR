using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>
/// 一次同步的完整计划：需要写入系统的计划任务定义、需要清理的任务名称，以及被跳过的原因。
/// </summary>
public sealed record WindowsScheduledTaskPlan(
    IReadOnlyList<WindowsScheduledTaskDefinition> Tasks,
    IReadOnlyList<string> ObsoleteTaskNames,
    IReadOnlyList<string> Warnings);

/// <summary>
/// 根据定时器快照决定需要创建、重建或删除哪些 Windows 计划任务。
/// 本类型不访问系统任务计划程序，决策结果由调用方执行。
/// </summary>
public static class WindowsScheduledTaskPlanner
{
    /// <summary>生成同步计划。</summary>
    /// <param name="timers">应用内定时器快照。</param>
    /// <param name="context">运行环境信息，其有效实例列表决定定时器是否可以创建计划任务。</param>
    /// <param name="managedTaskNames">系统任务文件夹中当前存在的任务名称。</param>
    public static WindowsScheduledTaskPlan CreatePlan(
        IReadOnlyCollection<WindowsScheduledTaskTimer> timers,
        WindowsScheduledTaskContext context,
        IReadOnlyCollection<string> managedTaskNames)
    {
        var scopeToken = WindowsScheduledTaskDefinitionBuilder.BuildScopeToken(context.ExecutablePath);
        var ownedPrefix = $"{WindowsScheduledTaskDefinitionBuilder.TaskNamePrefix}{scopeToken}.";
        var validInstanceIds = new HashSet<string>(context.ValidInstanceIds, StringComparer.OrdinalIgnoreCase);

        var tasks = new List<WindowsScheduledTaskDefinition>();
        var warnings = new List<string>();

        foreach (var timer in timers.OrderBy(candidate => candidate.TimerId))
        {
            var displayName = $"{WindowsScheduledTaskDefinitionBuilder.BuildTaskName(scopeToken, timer.TimerId)}";

            if (!timer.IsEnabled)
                continue;

            if (!timer.IsStartTask)
            {
                warnings.Add($"[计划任务] {displayName} 的动作是停止任务，不创建系统计划任务。");
                continue;
            }

            if (!WindowsScheduledTaskDefinitionBuilder.IsRepeatConfigured(timer))
            {
                warnings.Add($"[计划任务] {displayName} 未选择任何重复日期，不创建系统计划任务。");
                continue;
            }

            if (string.IsNullOrWhiteSpace(timer.InstanceId))
            {
                warnings.Add($"[计划任务] {displayName} 未选择实例，不创建系统计划任务。");
                continue;
            }

            if (!validInstanceIds.Contains(timer.InstanceId))
            {
                warnings.Add($"[计划任务] {displayName} 选择的实例 {timer.InstanceId} 已失效，不创建系统计划任务。");
                continue;
            }

            tasks.Add(new WindowsScheduledTaskDefinition(
                timer.TimerId,
                displayName,
                WindowsScheduledTaskDefinitionBuilder.BuildXml(timer, context),
                WindowsScheduledTaskDefinitionBuilder.ComputeFingerprint(timer, context)));
        }

        var desiredTaskNames = tasks
            .Select(task => task.TaskName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 只清理本安装目录归属、且当前不再需要的任务，其他安装目录创建的任务保持不动。
        var obsoleteTaskNames = managedTaskNames
            .Where(name => name.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(name => !desiredTaskNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WindowsScheduledTaskPlan(tasks, obsoleteTaskNames, warnings);
    }
}
