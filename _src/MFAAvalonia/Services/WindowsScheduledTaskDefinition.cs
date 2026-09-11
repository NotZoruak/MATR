using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace MFAAvalonia.Services;

/// <summary>定时器的重复方式，对应 Windows 计划任务的三类日历触发器。</summary>
public enum WindowsScheduledTaskRepeatType
{
    /// <summary>每天触发</summary>
    Daily = 0,

    /// <summary>按周触发</summary>
    Weekly = 1,

    /// <summary>按月触发</summary>
    Monthly = 2
}

/// <summary>
/// 生成计划任务所需的单个定时器快照，只依赖基础类型，便于单元测试。
/// </summary>
public sealed record WindowsScheduledTaskTimer(
    int TimerId,
    bool IsEnabled,
    TimeSpan Time,
    bool IsStartTask,
    string? InstanceId,
    WindowsScheduledTaskRepeatType RepeatType,
    IReadOnlyList<DayOfWeek> DaysOfWeek,
    IReadOnlyList<int> DaysOfMonth);

/// <summary>生成计划任务所需的运行环境信息。</summary>
public sealed record WindowsScheduledTaskContext(
    string ExecutablePath,
    string WorkingDirectory,
    bool ForceScheduledStart,
    IReadOnlyCollection<string> ValidInstanceIds,
    DateTime Now);

/// <summary>已生成完成、可以直接写入系统任务计划程序的计划任务定义。</summary>
public sealed record WindowsScheduledTaskDefinition(
    int TimerId,
    string TaskName,
    string Xml,
    string Fingerprint);

/// <summary>
/// 把应用内定时器转换为 Windows 计划任务定义。
/// 本类型不访问系统任务计划程序，只负责生成任务名称、命令行参数与任务 XML。
/// </summary>
public static class WindowsScheduledTaskDefinitionBuilder
{
    /// <summary>MATR 计划任务所在的系统任务文件夹，应用只管理该文件夹。</summary>
    public const string TaskFolderName = "MATR";

    /// <summary>计划任务名称前缀，应用只管理该前缀下的任务。</summary>
    public const string TaskNamePrefix = "MATR.Timer.";

    /// <summary>任务说明中的指纹标记，用于判断已有任务是否需要重建。</summary>
    public const string FingerprintMarker = "MATR-Timer-Fingerprint:";

    private const string FingerprintPlaceholder = "MATR-FINGERPRINT-PLACEHOLDER";
    private const int FingerprintLength = 16;

    /// <summary>计算指纹时使用的固定开始边界日期，避免日期变化导致任务每天重建。</summary>
    private static readonly DateTime FingerprintBoundaryDate = new(2000, 1, 1);

    private static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>生成安装目录归属令牌，用于区分同一台电脑上的多个 MATR 安装目录。</summary>
    public static string BuildScopeToken(string? executablePath)
    {
        var normalized = executablePath?.Trim() ?? string.Empty;
        try
        {
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                normalized = Path.GetFullPath(normalized)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
        catch (Exception)
        {
            // 路径无法规范化时直接使用原始文本，保证令牌仍然可用。
        }

        if (OperatingSystem.IsWindows())
            normalized = normalized.ToUpperInvariant();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    /// <summary>生成计划任务名称，定时器序号从 1 开始显示。</summary>
    public static string BuildTaskName(string scopeToken, int timerId)
    {
        return $"{TaskNamePrefix}{scopeToken}.{timerId + 1}";
    }

    /// <summary>生成包含任务文件夹的完整任务路径。</summary>
    public static string BuildTaskPath(string taskName)
    {
        return $"{TaskFolderName}\\{taskName}";
    }

    /// <summary>生成启动 MATR 的命令行参数。</summary>
    public static string BuildArguments(string instanceId, bool forceScheduledStart)
    {
        var arguments = $"--autostart --instance \"{instanceId}\"";
        return forceScheduledStart ? $"{arguments} --forceStart" : arguments;
    }

    /// <summary>判断定时器的重复规则是否至少命中一天，未命中时不会创建计划任务。</summary>
    public static bool IsRepeatConfigured(WindowsScheduledTaskTimer timer)
    {
        return timer.RepeatType switch
        {
            WindowsScheduledTaskRepeatType.Daily => true,
            WindowsScheduledTaskRepeatType.Weekly => NormalizeDaysOfWeek(timer).Count > 0,
            WindowsScheduledTaskRepeatType.Monthly => NormalizeDaysOfMonth(timer).Count > 0,
            _ => false
        };
    }

    /// <summary>生成完整的计划任务 XML。</summary>
    public static string BuildXml(WindowsScheduledTaskTimer timer, WindowsScheduledTaskContext context)
    {
        return BuildTaskXml(timer, context, ComputeFingerprint(timer, context), GetStartBoundary(timer, context));
    }

    /// <summary>
    /// 计算定时器的配置指纹。指纹只取决于可感知配置与任务模板，
    /// 不包含开始边界日期，因此同一个定时器不会因为日期推进而每天重建。
    /// </summary>
    public static string ComputeFingerprint(WindowsScheduledTaskTimer timer, WindowsScheduledTaskContext context)
    {
        // 使用固定日期加上定时时间作为规范边界：日期变化不影响指纹，定时时间变化必须重建任务。
        var canonicalBoundary = FingerprintBoundaryDate.Date.Add(NormalizeTime(timer.Time));
        var canonical = BuildTaskXml(timer, context, FingerprintPlaceholder, canonicalBoundary);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash)[..FingerprintLength].ToLowerInvariant();
    }

    /// <summary>从已有任务的 XML 中读取指纹，读取不到时返回 null。</summary>
    public static string? TryReadFingerprint(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        var markerIndex = xml.IndexOf(FingerprintMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return null;

        var start = markerIndex + FingerprintMarker.Length;
        var end = start;
        while (end < xml.Length && Uri.IsHexDigit(xml[end]))
            end++;

        return end > start ? xml[start..end] : null;
    }

    /// <summary>把界面上的重复方式映射为计划任务重复方式。</summary>
    public static WindowsScheduledTaskRepeatType ToRepeatType(int scheduleType)
    {
        return scheduleType switch
        {
            1 => WindowsScheduledTaskRepeatType.Weekly,
            2 => WindowsScheduledTaskRepeatType.Monthly,
            _ => WindowsScheduledTaskRepeatType.Daily
        };
    }

    private static string BuildTaskXml(
        WindowsScheduledTaskTimer timer,
        WindowsScheduledTaskContext context,
        string fingerprint,
        DateTime startBoundary)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(TaskNamespace + "Task",
                new XAttribute("version", "1.4"),
                new XElement(TaskNamespace + "RegistrationInfo",
                    new XElement(TaskNamespace + "Description", BuildDescription(timer, fingerprint))),
                new XElement(TaskNamespace + "Triggers", BuildTrigger(timer, startBoundary)),
                new XElement(TaskNamespace + "Principals",
                    new XElement(TaskNamespace + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(TaskNamespace + "LogonType", "InteractiveToken"),
                        new XElement(TaskNamespace + "RunLevel", "LeastPrivilege"))),
                BuildSettings(),
                new XElement(TaskNamespace + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(TaskNamespace + "Exec",
                        new XElement(TaskNamespace + "Command", context.ExecutablePath),
                        new XElement(TaskNamespace + "Arguments",
                            BuildArguments(timer.InstanceId ?? string.Empty, context.ForceScheduledStart)),
                        new XElement(TaskNamespace + "WorkingDirectory", context.WorkingDirectory)))));

        return document.ToString();
    }

    private static string BuildDescription(WindowsScheduledTaskTimer timer, string fingerprint)
    {
        return $"MATR 定时唤醒 | 实例 {timer.InstanceId} | {FingerprintMarker}{fingerprint}";
    }

    private static XElement BuildSettings()
    {
        return new XElement(TaskNamespace + "Settings",
            // 必须使用 Parallel：MATR 被系统计划任务拉起后会一直运行，
            // 若使用 IgnoreNew，任务实例会长期保持「正在运行」，后续每天的触发都会被系统忽略。
            new XElement(TaskNamespace + "MultipleInstancesPolicy", "Parallel"),
            new XElement(TaskNamespace + "DisallowStartIfOnBatteries", "false"),
            new XElement(TaskNamespace + "StopIfGoingOnBatteries", "false"),
            new XElement(TaskNamespace + "AllowHardTerminate", "true"),
            new XElement(TaskNamespace + "StartWhenAvailable", "true"),
            new XElement(TaskNamespace + "RunOnlyIfNetworkAvailable", "false"),
            new XElement(TaskNamespace + "IdleSettings",
                new XElement(TaskNamespace + "StopOnIdleEnd", "false"),
                new XElement(TaskNamespace + "RestartOnIdle", "false")),
            new XElement(TaskNamespace + "AllowStartOnDemand", "true"),
            new XElement(TaskNamespace + "Enabled", "true"),
            new XElement(TaskNamespace + "Hidden", "false"),
            new XElement(TaskNamespace + "RunOnlyIfIdle", "false"),
            new XElement(TaskNamespace + "WakeToRun", "false"),
            new XElement(TaskNamespace + "ExecutionTimeLimit", "PT0S"),
            new XElement(TaskNamespace + "Priority", "7"));
    }

    private static XElement BuildTrigger(WindowsScheduledTaskTimer timer, DateTime startBoundary)
    {
        var boundaryText = startBoundary.ToString("yyyy-MM-ddTHH:mm:ss");
        return new XElement(TaskNamespace + "CalendarTrigger",
            new XElement(TaskNamespace + "StartBoundary", boundaryText),
            new XElement(TaskNamespace + "Enabled", "true"),
            BuildSchedule(timer));
    }

    private static XElement BuildSchedule(WindowsScheduledTaskTimer timer)
    {
        switch (timer.RepeatType)
        {
            case WindowsScheduledTaskRepeatType.Weekly:
                return new XElement(TaskNamespace + "ScheduleByWeek",
                    new XElement(TaskNamespace + "WeeksInterval", "1"),
                    new XElement(TaskNamespace + "DaysOfWeek",
                        NormalizeDaysOfWeek(timer).Select(day =>
                            new XElement(TaskNamespace + day.ToString()))));

            case WindowsScheduledTaskRepeatType.Monthly:
                return new XElement(TaskNamespace + "ScheduleByMonth",
                    new XElement(TaskNamespace + "DaysOfMonth",
                        NormalizeDaysOfMonth(timer).Select(day =>
                            new XElement(TaskNamespace + "Day", day))),
                    new XElement(TaskNamespace + "Months",
                        Enumerable.Range(1, 12).Select(month =>
                            new XElement(TaskNamespace + ((MonthOfYear)month).ToString()))));

            default:
                return new XElement(TaskNamespace + "ScheduleByDay",
                    new XElement(TaskNamespace + "DaysInterval", "1"));
        }
    }

    private static IReadOnlyList<DayOfWeek> NormalizeDaysOfWeek(WindowsScheduledTaskTimer timer)
    {
        return timer.DaysOfWeek
            .Distinct()
            .OrderBy(day => (int)day)
            .ToList();
    }

    private static IReadOnlyList<int> NormalizeDaysOfMonth(WindowsScheduledTaskTimer timer)
    {
        return timer.DaysOfMonth
            .Where(day => day is >= 1 and <= 31)
            .Distinct()
            .OrderBy(day => day)
            .ToList();
    }

    private static DateTime GetStartBoundary(WindowsScheduledTaskTimer timer, WindowsScheduledTaskContext context)
    {
        var time = NormalizeTime(timer.Time);
        var boundary = context.Now.Date.Add(time);
        return boundary <= context.Now ? boundary.AddDays(1) : boundary;
    }

    private static TimeSpan NormalizeTime(TimeSpan time)
    {
        return time < TimeSpan.Zero || time >= TimeSpan.FromDays(1) ? TimeSpan.Zero : time;
    }

    private enum MonthOfYear
    {
        January = 1,
        February = 2,
        March = 3,
        April = 4,
        May = 5,
        June = 6,
        July = 7,
        August = 8,
        September = 9,
        October = 10,
        November = 11,
        December = 12
    }
}
