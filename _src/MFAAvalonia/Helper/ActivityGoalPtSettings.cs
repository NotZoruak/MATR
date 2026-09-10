using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MFAAvalonia.Helper;

/// <summary>
/// 活动目标 PT 的用户配置，来源于任务面板的选项链。
/// </summary>
public readonly record struct ActivityGoalPtSettings(
    bool Enabled,
    long TotalGoal,
    bool UseDatePlan,
    DateOnly? StartDay,
    DateOnly? EndDay)
{
    /// <summary>目标总 PT 下界，避免用户填入非正数导致目标恒为达标。</summary>
    public const long MinimumTotalGoal = 1;

    /// <summary>目标总 PT 的默认值，与选项定义保持一致。</summary>
    public const long DefaultTotalGoal = 30000;

    /// <summary>
    /// 从选项链解析配置。任一必需项缺失或非法时返回禁用状态，不参与提前结束判断。
    /// </summary>
    /// <param name="enabledCases">「达到目标Pt后跳过任务」的已选 case。</param>
    /// <param name="totalGoalText">目标总 PT 的输入文本。</param>
    /// <param name="datePlanCases">「使用日期计划」的已选 case。</param>
    /// <param name="startDateText">开始日期的输入文本。</param>
    /// <param name="endDateText">截至日期的输入文本。</param>
    public static ActivityGoalPtSettings Parse(
        IEnumerable<string>? enabledCases,
        string? totalGoalText,
        IEnumerable<string>? datePlanCases,
        string? startDateText,
        string? endDateText)
    {
        if (enabledCases?.Contains("Yes") != true)
            return Disabled();

        var totalGoal = ParseTotalGoal(totalGoalText);
        if (totalGoal < MinimumTotalGoal)
            return Disabled();

        if (datePlanCases?.Contains("Yes") != true)
            return new ActivityGoalPtSettings(true, totalGoal, false, null, null);

        if (!TryParseDate(startDateText, out var startDay) || !TryParseDate(endDateText, out var endDay))
            return Disabled();

        // 开始日期不得晚于截至日期
        if (startDay > endDay)
            return Disabled();

        return new ActivityGoalPtSettings(true, totalGoal, true, startDay, endDay);
    }

    private static ActivityGoalPtSettings Disabled() => new(false, 0, false, null, null);

    private static long ParseTotalGoal(string? text) =>
        long.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : DefaultTotalGoal;

    private static bool TryParseDate(string? text, out DateOnly day) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out day);
}
