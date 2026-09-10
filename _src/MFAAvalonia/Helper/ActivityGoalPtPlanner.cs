using System;

namespace MFAAvalonia.Helper;

/// <summary>
/// 按计划日推进的活动目标 PT 计算。
/// 只负责日期边界与目标值推导，不读取配置界面，也不识别游戏画面。
/// </summary>
public static class ActivityGoalPtPlanner
{
    /// <summary>计划日切换的小时数；本地时间该点之前仍属于前一个计划日。</summary>
    public const int PlanDayBoundaryHour = 5;

    /// <summary>
    /// 把本地时间换算为计划日日期：以每日 05:00 为界，05:00 前属于上一个计划日。
    /// </summary>
    public static DateOnly GetPlanDay(DateTime localTime) =>
        DateOnly.FromDateTime(localTime.AddHours(-PlanDayBoundaryHour));

    /// <summary>
    /// 计算当前计划日的绝对目标 PT；计划尚未开始时返回 null，表示不进行检查。
    /// </summary>
    /// <param name="totalGoal">用户配置的目标总 PT，必须为正数。</param>
    /// <param name="startDay">计划开始日期。</param>
    /// <param name="endDay">计划截至日期，不得早于开始日期。</param>
    /// <param name="currentDay">当前计划日。</param>
    public static long? GetAbsoluteGoal(long totalGoal, DateOnly startDay, DateOnly endDay, DateOnly currentDay)
    {
        if (totalGoal <= 0 || endDay < startDay)
            return null;

        // 计划尚未开始时不进行检查
        if (currentDay < startDay)
            return null;

        // 到达或超过截至日时固定为总目标
        if (currentDay >= endDay)
            return totalGoal;

        var totalDays = endDay.DayNumber - startDay.DayNumber + 1;
        var elapsedDays = currentDay.DayNumber - startDay.DayNumber + 1;

        // 向上取整，确保区间内每一天都不会低于按比例分配的目标
        var numerator = (decimal)totalGoal * elapsedDays;
        return (long)Math.Ceiling(numerator / totalDays);
    }

    /// <summary>
    /// 判断当前累计 PT 是否已达到当前计划日的绝对目标。
    /// 目标无法计算（配置无效或计划未开始）或 PT 识别失败时返回 false，绝不判定达标。
    /// </summary>
    public static bool IsGoalReached(long? currentPt, long? absoluteGoal) =>
        currentPt.HasValue && absoluteGoal.HasValue && currentPt.Value >= absoluteGoal.Value;
}
