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
    /// <param name="staleScopeToken">
    /// 上一次同步所在安装目录的归属令牌。安装目录整体挪走、旧路径已不存在时传入，
    /// 旧令牌下的计划任务会被一并清理，避免留下永远失败的残留任务。
    /// </param>
    public static WindowsScheduledTaskPlan CreatePlan(
        IReadOnlyCollection<WindowsScheduledTaskTimer> timers,
        WindowsScheduledTaskContext context,
        IReadOnlyCollection<string> managedTaskNames,
        string? staleScopeToken = null)
    {
        var scopeToken = WindowsScheduledTaskDefinitionBuilder.BuildScopeToken(context.ExecutablePath);
        var ownedPrefix = $"{WindowsScheduledTaskDefinitionBuilder.TaskNamePrefix}{scopeToken}.";
        var stalePrefix = string.IsNullOrWhiteSpace(staleScopeToken)
            ? null
            : $"{WindowsScheduledTaskDefinitionBuilder.TaskNamePrefix}{staleScopeToken}.";
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

        // 只清理本安装目录归属、以及安装目录已经被挪走的上一次归属，
        // 且当前不再需要的任务；其他安装目录创建的任务保持不动。
        var obsoleteTaskNames = managedTaskNames
            .Where(name => name.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase)
                || (stalePrefix != null && name.StartsWith(stalePrefix, StringComparison.OrdinalIgnoreCase)))
            .Where(name => !desiredTaskNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WindowsScheduledTaskPlan(tasks, obsoleteTaskNames, warnings);
    }

    /// <summary>
    /// 判断是否需要清理上一次同步所在安装目录留下的计划任务。
    /// 安装目录整体移动或重命名后，旧任务的动作仍指向旧路径；只有确认旧路径已经不存在时才清理，
    /// 避免把另一份仍然存在的安装（例如复制出来的副本）的任务误删。
    /// </summary>
    /// <param name="currentScopeToken">当前安装目录的归属令牌。</param>
    /// <param name="storedScopeToken">上一次同步记录下来的归属令牌。</param>
    /// <param name="storedExecutablePath">上一次同步记录下来的可执行文件路径。</param>
    /// <param name="pathExists">判断路径是否仍然存在的委托，便于测试与替换。</param>
    public static string? ResolveStaleScopeToken(
        string currentScopeToken,
        string? storedScopeToken,
        string? storedExecutablePath,
        Func<string, bool> pathExists)
    {
        if (string.IsNullOrWhiteSpace(storedScopeToken)
            || string.IsNullOrWhiteSpace(storedExecutablePath)
            || string.Equals(storedScopeToken, currentScopeToken, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            // 旧安装仍然存在（例如用户复制了整份目录）时不能动它的计划任务。
            return pathExists(storedExecutablePath) ? null : storedScopeToken;
        }
        catch (Exception)
        {
            // 无法判断时按"旧安装仍然存在"处理：宁可留下残留，也不误删。
            return null;
        }
    }
}
