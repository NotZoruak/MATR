using MaaFramework.Binding;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Pages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.Services;

/// <summary>
/// 协调定时触发与正在运行的任务队列之间的交接。
/// </summary>
public static class ScheduledTaskHandoffService
{
    private static readonly TimeSpan NaturalCompletionTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StatePollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> HandoffLocks = new(StringComparer.Ordinal);

    /// <summary>
    /// 按强制执行设置等待或停止冲突队列，并在全部冲突解除后启动定时目标。
    /// </summary>
    public static async Task ExecuteAsync(
        TaskQueueViewModel targetViewModel,
        bool forceExecution,
        bool finishCurrentOrdinaryTask,
        Func<Task> startTarget)
    {
        ArgumentNullException.ThrowIfNull(targetViewModel);
        ArgumentNullException.ThrowIfNull(startTarget);

        var locks = GetCoordinationLocks(targetViewModel);
        var acquiredLocks = new List<SemaphoreSlim>(locks.Count);
        try
        {
            foreach (var handoffLock in locks)
            {
                await handoffLock.WaitAsync();
                acquiredLocks.Add(handoffLock);
            }

            var conflicts = GetActiveConflicts(targetViewModel);
            if (conflicts.Count == 0)
            {
                await StartTargetAsync(startTarget);
                return;
            }

            var conflictIds = conflicts
                .Select(viewModel => viewModel.Processor.InstanceId)
                .ToHashSet(StringComparer.Ordinal);

            if (!forceExecution)
            {
                var specialConflicts = conflicts
                    .Where(viewModel => viewModel.Processor.HasActiveSpecialTask)
                    .ToList();
                if (specialConflicts.Count > 0)
                {
                    LoggerHelper.Warning(
                        $"[定时执行] 存在运行中的特殊任务，本次定时启动已取消并保留原任务：{DescribeInstances(specialConflicts)}");
                    return;
                }

                if (!await WaitForNaturalCompletionAsync(targetViewModel))
                {
                    LoggerHelper.Warning(
                        $"[定时执行] 冲突任务未在 10 分钟内自然结束，本次定时启动已取消并保留原任务：{DescribeInstanceIds(conflictIds)}");
                    return;
                }

                LoggerHelper.Info($"[定时执行] 冲突任务已自然结束，启动目标实例 {targetViewModel.Processor.InstanceId}");
                await StartTargetAsync(startTarget);
                return;
            }

            await StopConflictsAndWaitAsync(targetViewModel, conflicts, finishCurrentOrdinaryTask);
            LoggerHelper.Info($"[定时执行] 冲突任务已停止，启动目标实例 {targetViewModel.Processor.InstanceId}");
            await StartTargetAsync(startTarget);
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[定时执行] 交接失败：{exception.Message}", exception);
        }
        finally
        {
            for (var index = acquiredLocks.Count - 1; index >= 0; index--)
                acquiredLocks[index].Release();
        }
    }

    private static List<SemaphoreSlim> GetCoordinationLocks(TaskQueueViewModel targetViewModel)
    {
        var keys = new List<string>
        {
            $"instance:{targetViewModel.Processor.InstanceId}"
        };
        var targetIdentity = GetControllerTargetIdentity(targetViewModel);
        if (targetIdentity != null)
            keys.Add($"device:{targetIdentity.ToUpperInvariant()}");

        return keys
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => HandoffLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1)))
            .ToList();
    }

    private static List<TaskQueueViewModel> GetActiveConflicts(TaskQueueViewModel targetViewModel)
    {
        var candidates = MaaProcessorManager.Instance.Instances
            .Select(processor => processor.ViewModel)
            .OfType<TaskQueueViewModel>()
            .Append(targetViewModel)
            .DistinctBy(viewModel => viewModel.Processor.InstanceId, StringComparer.Ordinal);

        return candidates
            .Where(IsTaskQueueActive)
            .Where(viewModel => IsSameInstanceOrDevice(viewModel, targetViewModel))
            .ToList();
    }

    private static bool IsTaskQueueActive(TaskQueueViewModel viewModel)
    {
        return viewModel.IsRunning
            || viewModel.Processor.IsTaskRunActive
            || viewModel.Processor.TaskQueue.Count > 0;
    }

    private static bool IsSameInstanceOrDevice(TaskQueueViewModel first, TaskQueueViewModel second)
    {
        if (string.Equals(first.Processor.InstanceId, second.Processor.InstanceId, StringComparison.Ordinal))
            return true;

        var firstIdentity = GetControllerTargetIdentity(first);
        var secondIdentity = GetControllerTargetIdentity(second);
        return firstIdentity != null
            && secondIdentity != null
            && string.Equals(firstIdentity, secondIdentity, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetControllerTargetIdentity(TaskQueueViewModel viewModel)
    {
        return viewModel.CurrentController switch
        {
            MaaControllerTypes.Adb => GetAdbIdentity(viewModel),
            MaaControllerTypes.Win32 or MaaControllerTypes.Gamepad => GetDesktopWindowIdentity(viewModel),
            MaaControllerTypes.MacOS => GetMacOSWindowIdentity(viewModel),
            MaaControllerTypes.WlRoots => GetWlRootsIdentity(viewModel),
            MaaControllerTypes.PlayCover => GetPlayCoverIdentity(viewModel),
            _ => null
        };
    }

    private static string? GetAdbIdentity(TaskQueueViewModel viewModel)
    {
        var device = viewModel.CurrentDevice as AdbDeviceInfo ?? viewModel.Processor.Config.AdbDevice.Info;
        var serial = device?.AdbSerial ?? viewModel.Processor.Config.AdbDevice.AdbSerial;
        return string.IsNullOrWhiteSpace(serial) ? null : $"adb:{serial.Trim()}";
    }

    private static string? GetDesktopWindowIdentity(TaskQueueViewModel viewModel)
    {
        var handle = (viewModel.CurrentDevice as DesktopWindowInfo)?.Handle
            ?? viewModel.Processor.Config.DesktopWindow.HWnd;
        return handle == IntPtr.Zero ? null : $"desktop-window:{handle.ToInt64():X}";
    }

    private static string? GetMacOSWindowIdentity(TaskQueueViewModel viewModel)
    {
        var windowId = (viewModel.CurrentDevice as MacOSWindowInfo)?.WindowId
            ?? viewModel.Processor.Config.MacOSWindow.WindowId;
        return windowId == 0 ? null : $"macos-window:{windowId}";
    }

    private static string? GetWlRootsIdentity(TaskQueueViewModel viewModel)
    {
        var socketPath = (viewModel.CurrentDevice as WlRootsSocketInfo)?.SocketPath
            ?? viewModel.Processor.Config.WlRoots.SocketPath;
        return string.IsNullOrWhiteSpace(socketPath) ? null : $"wlroots:{socketPath.Trim()}";
    }

    private static string? GetPlayCoverIdentity(TaskQueueViewModel viewModel)
    {
        var address = viewModel.Processor.Config.PlayCover.PlayCoverAddress;
        return string.IsNullOrWhiteSpace(address) ? null : $"playcover:{address.Trim()}";
    }

    private static async Task<bool> WaitForNaturalCompletionAsync(TaskQueueViewModel targetViewModel)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < NaturalCompletionTimeout)
        {
            var activeConflicts = GetActiveConflicts(targetViewModel);
            if (activeConflicts.Count == 0)
                return true;

            var specialConflict = activeConflicts.Any(viewModel => viewModel.Processor.HasActiveSpecialTask);
            if (specialConflict)
            {
                LoggerHelper.Warning(
                    $"[定时执行] 等待期间出现运行中的特殊任务，本次定时启动已取消并保留原任务：{DescribeInstances(activeConflicts)}");
                return false;
            }

            await Task.Delay(StatePollInterval);
        }

        return GetActiveConflicts(targetViewModel).Count == 0;
    }

    private static async Task StopConflictsAndWaitAsync(
        TaskQueueViewModel targetViewModel,
        IReadOnlyCollection<TaskQueueViewModel> initialConflicts,
        bool finishCurrentOrdinaryTask)
    {
        var requestedGenerations = new Dictionary<string, long>(StringComparer.Ordinal);
        RequestStopForConflicts(initialConflicts, finishCurrentOrdinaryTask, requestedGenerations);

        while (true)
        {
            await Task.Delay(StatePollInterval);

            var activeConflicts = GetActiveConflicts(targetViewModel);
            if (activeConflicts.Count == 0)
                return;

            var unhandledConflicts = activeConflicts
                .Where(viewModel => !requestedGenerations.TryGetValue(viewModel.Processor.InstanceId, out var generation)
                    || generation != viewModel.Processor.TaskRunGeneration)
                .ToList();
            if (unhandledConflicts.Count > 0)
                RequestStopForConflicts(unhandledConflicts, finishCurrentOrdinaryTask, requestedGenerations);
        }
    }

    private static void RequestStopForConflicts(
        IReadOnlyCollection<TaskQueueViewModel> conflicts,
        bool finishCurrentOrdinaryTask,
        IDictionary<string, long> requestedGenerations)
    {
        foreach (var viewModel in conflicts)
        {
            var instanceId = viewModel.Processor.InstanceId;
            if (finishCurrentOrdinaryTask
                && viewModel.Processor.HasActiveOrdinaryTask
                && viewModel.RequestStopAfterCurrentOrdinaryTask(static () => { }))
            {
                LoggerHelper.Info(
                    $"[定时执行] 普通任务完成当前一圈后停止实例 {instanceId} 的任务队列");
                requestedGenerations[instanceId] = viewModel.Processor.TaskRunGeneration;
                continue;
            }

            LoggerHelper.Info($"[定时执行] 正在停止冲突实例 {instanceId}");
            viewModel.StopTask();
            requestedGenerations[instanceId] = viewModel.Processor.TaskRunGeneration;
        }
    }

    private static Task StartTargetAsync(Func<Task> startTarget)
    {
        return DispatcherHelper.RunOnMainThreadAsync<Task>(startTarget).Unwrap();
    }

    private static string DescribeInstances(IEnumerable<TaskQueueViewModel> viewModels)
    {
        return DescribeInstanceIds(viewModels.Select(viewModel => viewModel.Processor.InstanceId));
    }

    private static string DescribeInstanceIds(IEnumerable<string> instanceIds)
    {
        return string.Join("、", instanceIds);
    }
}
