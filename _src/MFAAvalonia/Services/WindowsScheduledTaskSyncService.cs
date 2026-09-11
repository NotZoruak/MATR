using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Other;

namespace MFAAvalonia.Services;

/// <summary>最近一次 Windows 计划任务同步的结果，供日志与设置界面展示。</summary>
public sealed record WindowsScheduledTaskSyncStatus(
    bool HasRun,
    bool Succeeded,
    int TaskCount,
    string Message,
    DateTime? CompletedAt)
{
    /// <summary>尚未执行过同步。</summary>
    public static WindowsScheduledTaskSyncStatus NotRun { get; } = new(false, true, 0, string.Empty, null);
}

/// <summary>
/// 在应用内定时器与 Windows 系统计划任务之间同步。
/// 系统计划任务只补足「MATR 未运行」的情形：到点启动当前安装目录的 MATR.exe，
/// 并复用既有命令行参数与单实例转发逻辑启动目标实例的任务。
/// 本类型不负责启动 MaaFramework、模拟器或任务队列，非 Windows 平台不执行任何操作。
/// </summary>
public static class WindowsScheduledTaskSyncService
{
    private static readonly object DebounceLock = new();
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// 系统计划任务客户端按需创建：非 Windows 平台不会构造任何 Windows 专用对象。
    /// </summary>
    private static readonly Lazy<IWindowsScheduledTaskClient> DefaultClient =
        new(() => new SchTasksScheduledTaskClient());

    private static IWindowsScheduledTaskClient? _clientOverride;
    private static Timer? _debounceTimer;
    private static int _pendingSync;
    private static int _isSyncing;
    private static bool _initialized;

    /// <summary>当前平台是否支持 Windows 计划任务同步。</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>最近一次同步结果。</summary>
    public static WindowsScheduledTaskSyncStatus Status { get; private set; } = WindowsScheduledTaskSyncStatus.NotRun;

    /// <summary>同步结果更新事件，界面据此刷新状态提示。</summary>
    public static event Action? StatusChanged;

    /// <summary>测试用入口：替换系统计划任务客户端。</summary>
    public static void UseClient(IWindowsScheduledTaskClient client)
    {
        _clientOverride = client;
    }

    /// <summary>
    /// 初始化同步服务。在 Windows 上接管应用内定时器的重新排程请求，
    /// 让修改时间、重复规则、实例或强制定时启动开关后立即同步系统计划任务。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized || !IsSupported)
            return;

        _initialized = true;
        PlatformTimerScheduler.RescheduleAll = RequestSync;
    }

    /// <summary>请求一次同步，多次请求会被合并为一次执行。</summary>
    public static void RequestSync()
    {
        if (!IsSupported)
            return;

        Interlocked.Exchange(ref _pendingSync, 1);
        ReArmDebounce();
    }

    /// <summary>立即执行一次同步，用于启动流程与设置界面刷新。</summary>
    public static void SyncNow()
    {
        if (!IsSupported)
            return;

        RunSync();
    }

    /// <summary>
    /// 退出程序前调用：补跑尚未执行的同步请求，并停止后续排程。
    /// 系统计划任务本身会保留，关闭程序不等于取消定时。
    /// </summary>
    public static void Shutdown()
    {
        if (!IsSupported)
            return;

        if (Interlocked.CompareExchange(ref _pendingSync, 0, 1) == 1)
            RunSync();

        if (Status.HasRun && Status.TaskCount > 0)
        {
            LoggerHelper.Info(
                $"[计划任务] 退出程序时保留 {Status.TaskCount} 个 Windows 计划任务：关闭程序不会取消定时，需要关闭定时开关或删除该定时。");
        }

        lock (DebounceLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }

    private static void RunSync()
    {
        if (!IsSupported)
            return;

        // 先清除待执行标记：本轮同步期间到达的请求会重新置位，由本轮结束时补跑。
        Interlocked.Exchange(ref _pendingSync, 0);

        if (Interlocked.Exchange(ref _isSyncing, 1) == 1)
        {
            Interlocked.Exchange(ref _pendingSync, 1);
            return;
        }

        try
        {
            var snapshot = CaptureSnapshot();
            if (snapshot == null)
                return;

            ExecuteSync(snapshot);
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[计划任务] 同步 Windows 计划任务失败：{exception.Message}", exception);
            Publish(new WindowsScheduledTaskSyncStatus(
                true,
                false,
                0,
                LangKeys.WindowsScheduledTaskSyncFailed.ToLocalizationFormatted(false, exception.Message),
                DateTime.Now));
        }
        finally
        {
            Interlocked.Exchange(ref _isSyncing, 0);

            // 同步期间的改动请求不能在结束后被丢掉，补跑一次。
            if (Interlocked.Exchange(ref _pendingSync, 0) == 1)
                ReArmDebounce();
        }
    }

    private static void ReArmDebounce()
    {
        lock (DebounceLock)
        {
            _debounceTimer ??= new Timer(_ => RunSync(), null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private static SyncSnapshot? CaptureSnapshot()
    {
        return DispatcherHelper.RunOnMainThread(() =>
        {
            var manager = MaaProcessorManager.Instance;
            var instances = manager.GetAllInstanceIdsAndNames();
            if (instances.Count == 0)
            {
                // 实例列表尚未加载时不能判断定时器选择的实例是否有效，直接跳过可避免误删计划任务。
                LoggerHelper.Info("[计划任务] 实例列表尚未加载，跳过 Windows 计划任务同步。");
                return null;
            }

            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                LoggerHelper.Warning("[计划任务] 无法获取当前程序路径，跳过 Windows 计划任务同步。");
                return null;
            }

            var timerModel = TimerModel.Instance;
            // 与 TimerModel.ExecuteTimerTask 一致：关闭「自定配置」时定时器跟随当前实例。
            var customConfig = timerModel.CustomConfig;
            var timers = timerModel.Timers
                .Select(timer => new WindowsScheduledTaskTimer(
                    timer.TimerId,
                    timer.IsOn,
                    timer.Time,
                    timer.TimerAction == TimerActionType.StartTask,
                    customConfig && !string.IsNullOrWhiteSpace(timer.TimerConfig)
                        ? timer.TimerConfig
                        : manager.Current?.InstanceId,
                    WindowsScheduledTaskDefinitionBuilder.ToRepeatType((int)timer.ScheduleConfig.ScheduleType),
                    timer.ScheduleConfig.SelectedDaysOfWeek.ToList(),
                    timer.ScheduleConfig.SelectedDaysOfMonth.ToList()))
                .ToList();

            var context = new WindowsScheduledTaskContext(
                executablePath,
                Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
                timerModel.ForceScheduledStart,
                instances.Select(instance => instance.Id).ToList(),
                DateTime.Now);

            return new SyncSnapshot(timers, context);
        });
    }

    private static void ExecuteSync(SyncSnapshot snapshot)
    {
        var client = _clientOverride ?? DefaultClient.Value;
        var managedTaskNames = client.ListManagedTaskNames();
        var scopeToken = WindowsScheduledTaskDefinitionBuilder.BuildScopeToken(snapshot.Context.ExecutablePath);
        var staleScopeToken = ResolveStaleScopeToken(scopeToken);
        var plan = WindowsScheduledTaskPlanner.CreatePlan(
            snapshot.Timers,
            snapshot.Context,
            managedTaskNames,
            staleScopeToken);

        if (staleScopeToken != null)
        {
            LoggerHelper.Info(
                $"[计划任务] 检测到安装目录已更换，将清理旧安装目录留下的计划任务：{WindowsScheduledTaskDefinitionBuilder.TaskNamePrefix}{staleScopeToken}.*");
        }

        foreach (var warning in plan.Warnings)
            LoggerHelper.Warning(warning);

        var existingTaskNames = new HashSet<string>(managedTaskNames, StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        var writtenTaskCount = 0;

        foreach (var definition in plan.Tasks)
        {
            if (existingTaskNames.Contains(definition.TaskName)
                && string.Equals(
                    WindowsScheduledTaskDefinitionBuilder.TryReadFingerprint(
                        client.TryReadTaskXml(definition.TaskName)),
                    definition.Fingerprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                // 计划任务与当前配置一致，无需重建。
                continue;
            }

            var result = client.CreateOrUpdate(definition.TaskName, definition.Xml);
            if (result.Succeeded)
            {
                writtenTaskCount++;
                LoggerHelper.Info($"[计划任务] 已同步 Windows 计划任务：{definition.TaskName}");
            }
            else
            {
                failures.Add($"{definition.TaskName}：{result.ErrorMessage}");
                LoggerHelper.Error($"[计划任务] 创建或更新 {definition.TaskName} 失败：{result.ErrorMessage}");
            }
        }

        foreach (var taskName in plan.ObsoleteTaskNames)
        {
            var result = client.Delete(taskName);
            if (result.Succeeded)
            {
                LoggerHelper.Info($"[计划任务] 已删除不再需要的 Windows 计划任务：{taskName}");
            }
            else
            {
                failures.Add($"{taskName}：{result.ErrorMessage}");
                LoggerHelper.Error($"[计划任务] 删除 {taskName} 失败：{result.ErrorMessage}");
            }
        }

        if (failures.Count == 0)
        {
            LoggerHelper.Info(
                $"[计划任务] Windows 计划任务同步完成：需要同步 {plan.Tasks.Count} 个，本次写入 {writtenTaskCount} 个，清理 {plan.ObsoleteTaskNames.Count} 个。");

            // 同步无失败时更新安装目录记录，供安装目录整体移动后清理旧任务使用。
            PersistScopeRecord(scopeToken, snapshot.Context.ExecutablePath);

            Publish(new WindowsScheduledTaskSyncStatus(
                true,
                true,
                plan.Tasks.Count,
                BuildSuccessMessage(plan.Tasks.Count),
                DateTime.Now));
            return;
        }

        var detail = string.Join("；", failures);
        Publish(new WindowsScheduledTaskSyncStatus(
            true,
            false,
            plan.Tasks.Count,
            LangKeys.WindowsScheduledTaskSyncFailed.ToLocalizationFormatted(false, detail),
            DateTime.Now));
    }

    private static string? ResolveStaleScopeToken(string scopeToken)
    {
        return WindowsScheduledTaskPlanner.ResolveStaleScopeToken(
            scopeToken,
            GlobalConfiguration.GetValue(ConfigurationKeys.WindowsScheduledTaskScope, string.Empty),
            GlobalConfiguration.GetValue(ConfigurationKeys.WindowsScheduledTaskExecutablePath, string.Empty),
            File.Exists);
    }

    /// <summary>
    /// 记录本次同步的安装目录身份。同步存在失败项时保留旧记录，
    /// 让下一次同步继续尝试清理旧安装目录留下的计划任务。
    /// </summary>
    private static void PersistScopeRecord(string scopeToken, string executablePath)
    {
        var storedScope = GlobalConfiguration.GetValue(ConfigurationKeys.WindowsScheduledTaskScope, string.Empty);
        var storedPath = GlobalConfiguration.GetValue(ConfigurationKeys.WindowsScheduledTaskExecutablePath, string.Empty);
        if (string.Equals(storedScope, scopeToken, StringComparison.OrdinalIgnoreCase)
            && string.Equals(storedPath, executablePath, StringComparison.Ordinal))
        {
            return;
        }

        GlobalConfiguration.SetValue(ConfigurationKeys.WindowsScheduledTaskScope, scopeToken);
        GlobalConfiguration.SetValue(ConfigurationKeys.WindowsScheduledTaskExecutablePath, executablePath);
    }

    private static string BuildSuccessMessage(int taskCount)
    {
        return taskCount > 0
            ? LangKeys.WindowsScheduledTaskSyncSuccess.ToLocalizationFormatted(false, taskCount.ToString())
            : LangKeys.WindowsScheduledTaskSyncEmpty.ToLocalization();
    }

    private static void Publish(WindowsScheduledTaskSyncStatus status)
    {
        Status = status;
        var handler = StatusChanged;
        if (handler == null)
            return;

        try
        {
            DispatcherHelper.PostOnMainThread(() => handler());
        }
        catch (Exception)
        {
            // 界面不可用（例如正在退出）时直接忽略状态刷新。
        }
    }

    private sealed record SyncSnapshot(
        IReadOnlyList<WindowsScheduledTaskTimer> Timers,
        WindowsScheduledTaskContext Context);
}
