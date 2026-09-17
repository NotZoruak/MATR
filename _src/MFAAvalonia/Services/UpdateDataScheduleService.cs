using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MFAAvalonia.Services;

/// <summary>
/// 处理更新数据任务的触发间隔判断与成功时间记录。
/// 成功时间按任务分别保存：写了任务备注（或显示名称）的任务各记各的，
/// 没写备注时退化为按「识别内容」区分，仓库与刀帐仍可用不同频率各自更新。
/// </summary>
public static class UpdateDataScheduleService
{
    /// <summary>识别内容选项的名称。</summary>
    public const string ScopeOptionName = "识别内容";

    /// <summary>触发间隔选项的名称。</summary>
    public const string IntervalOptionName = "触发间隔";

    /// <summary>识别内容的默认 case，与 interface.json 中的 default_case 一致。</summary>
    public const string DefaultScope = "仓库+刀帐";

    /// <summary>触发间隔的默认 case，与 interface.json 中的 default_case 一致。</summary>
    public const string DefaultInterval = "每天";

    /// <summary>按任务备注与显示名称区分记录时的键前缀。</summary>
    private const string TaskKeyPrefix = "任务:";

    /// <summary>按识别内容区分记录时的键前缀。</summary>
    private const string ScopeKeyPrefix = "范围:";

    /// <summary>调度记录的键：StorageKey 用于落盘，DisplayName 用于界面与日志。</summary>
    public readonly record struct ScheduleKey(string StorageKey, string DisplayName);

    /// <summary>某条调度记录的上次成功时间发生变化时通知界面刷新；参数为实例标识与调度键。</summary>
    public static event Action<string, string>? LastSucceededChanged;

    /// <summary>把识别范围规范化为台账键，空值时归入默认范围。</summary>
    public static string NormalizeScope(string? scope) =>
        string.IsNullOrWhiteSpace(scope) ? DefaultScope : scope.Trim();

    /// <summary>
    /// 读取任务选项列表中某个 select 选项当前选中的 case 名称；
    /// 未选中时回退到该选项的 default_case，仍取不到则返回 null。
    /// </summary>
    public static string? GetSelectedCaseName(
        IEnumerable<MaaInterface.MaaInterfaceSelectOption>? options,
        string optionName)
    {
        if (MaaProcessor.Interface?.Option?.TryGetValue(optionName, out var definition) != true)
            return null;

        var index = options?.FirstOrDefault(option => option.Name == optionName)?.Index;
        if (index is int value && value >= 0 && definition.Cases is { } cases && value < cases.Count)
            return cases[value].Name;

        return definition.DefaultCase;
    }

    /// <summary>读取任务当前生效的触发间隔，取不到时按默认间隔处理。</summary>
    public static string ResolveInterval(IEnumerable<MaaInterface.MaaInterfaceSelectOption>? options) =>
        GetSelectedCaseName(options, IntervalOptionName) ?? DefaultInterval;

    /// <summary>
    /// 解析任务的调度键：优先用任务备注与显示名称，二者都为空时按识别内容区分，
    /// 这样同一队列里的两个更新数据任务只要写了备注就互不影响。
    /// </summary>
    public static ScheduleKey ResolveKey(MaaInterface.MaaInterfaceTask? interfaceItem)
    {
        var displayName = interfaceItem?.DisplayNameOverride;
        if (!string.IsNullOrWhiteSpace(displayName))
            return new ScheduleKey($"{TaskKeyPrefix}{displayName.Trim()}", displayName.Trim());

        var remark = interfaceItem?.Remark;
        if (!string.IsNullOrWhiteSpace(remark))
            return new ScheduleKey($"{TaskKeyPrefix}{remark.Trim()}", remark.Trim());

        var scope = NormalizeScope(GetSelectedCaseName(interfaceItem?.Option, ScopeOptionName));
        return new ScheduleKey($"{ScopeKeyPrefix}{scope}", scope);
    }

    /// <summary>把调度键规范化为台账键，空值时归入默认识别范围。</summary>
    public static string NormalizeKey(string? scheduleKey) =>
        string.IsNullOrWhiteSpace(scheduleKey) ? $"{ScopeKeyPrefix}{DefaultScope}" : scheduleKey.Trim();

    /// <summary>根据触发间隔判断指定调度键当前是否需要执行。</summary>
    public static bool ShouldRun(InstanceConfiguration configuration, string scheduleKey, string interval, DateTime now)
    {
        if (string.Equals(interval, "每次", StringComparison.Ordinal))
            return true;

        var lastSucceeded = GetLastSucceeded(configuration, scheduleKey);
        if (lastSucceeded == null)
            return true;

        var currentLocal = NormalizeToLocalTime(now);
        var lastLocal = NormalizeToLocalTime(lastSucceeded.Value);
        return interval switch
        {
            "每天" => currentLocal.Date != lastLocal.Date,
            "每周" => GetIsoWeekKey(currentLocal) != GetIsoWeekKey(lastLocal),
            _ => true,
        };
    }

    /// <summary>读取指定调度键上次成功完成的时间；默认识别范围兼容旧版的单键记录。</summary>
    public static DateTime? GetLastSucceeded(InstanceConfiguration configuration, string scheduleKey)
    {
        var normalizedKey = NormalizeKey(scheduleKey);
        var rawValue = configuration.GetValue(GetStorageKey(normalizedKey), string.Empty);
        if (string.IsNullOrWhiteSpace(rawValue)
            && string.Equals(normalizedKey, $"{ScopeKeyPrefix}{DefaultScope}", StringComparison.Ordinal))
        {
            // 旧版只有一个更新时间，归入默认识别范围，避免升级当天又完整跑一次
            rawValue = configuration.GetValue(ConfigurationKeys.UpdateDataLastSucceededAt, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        return DateTime.TryParse(
            rawValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>记录指定调度键本次成功完成的时间。</summary>
    public static void MarkSucceeded(InstanceConfiguration configuration, string scheduleKey, DateTime now)
    {
        var normalizedKey = NormalizeKey(scheduleKey);
        configuration.SetValue(
            GetStorageKey(normalizedKey),
            now.ToString("O", CultureInfo.InvariantCulture));
        LastSucceededChanged?.Invoke(configuration.InstanceId, normalizedKey);
    }

    /// <summary>把上次成功完成时间转换为本地时区的显示文本，供设置页与日志共用。</summary>
    public static string FormatLocalTime(DateTime value) =>
        NormalizeToLocalTime(value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>调度键对应的实例配置键。</summary>
    private static string GetStorageKey(string scheduleKey) =>
        $"{ConfigurationKeys.UpdateDataLastSucceededAt}.{scheduleKey}";

    private static DateTime NormalizeToLocalTime(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value.ToLocalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Local),
        _ => value,
    };

    private static (int Year, int Week) GetIsoWeekKey(DateTime value) =>
        (ISOWeek.GetYear(value), ISOWeek.GetWeekOfYear(value));
}
