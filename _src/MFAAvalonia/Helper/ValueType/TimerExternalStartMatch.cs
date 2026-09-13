using System;

namespace MFAAvalonia.Helper.ValueType;

/// <summary>
/// 外部定时启动（Windows 计划任务或命令行 --autostart）与应用内定时器的去重判定。
/// 只做纯计算、不依赖配置与界面类型，便于单独验证。
/// </summary>
public static class TimerExternalStartMatch
{
    /// <summary>
    /// 判断一次外部启动是否应视作「该定时器在本分钟已经触发」。
    /// 命中的定时器由调用方回写触发标记，避免应用内计时器在同一分钟重复触发同一个定时器。
    /// </summary>
    /// <param name="isEnabled">定时器是否启用。</param>
    /// <param name="isStartTask">定时器动作是否为「开始任务」。</param>
    /// <param name="timerTime">定时器设定时间。</param>
    /// <param name="scheduleMatches">重复规则在本次启动当天是否命中。</param>
    /// <param name="timerInstanceId">定时器目标实例 ID，为空表示跟随当前实例。</param>
    /// <param name="startedInstanceId">本次外部启动的实例 ID。</param>
    /// <param name="triggeredAt">本次外部启动的时间。</param>
    public static bool Covers(
        bool isEnabled,
        bool isStartTask,
        TimeSpan timerTime,
        bool scheduleMatches,
        string? timerInstanceId,
        string? startedInstanceId,
        DateTime triggeredAt)
    {
        if (!isEnabled || !isStartTask)
            return false;

        // 只有同一分钟内的定时器才会被这次外部启动覆盖，跨分钟的定时器必须保留。
        if (timerTime.Hours != triggeredAt.Hour || timerTime.Minutes != triggeredAt.Minute)
            return false;

        if (!scheduleMatches || string.IsNullOrWhiteSpace(startedInstanceId))
            return false;

        // 跟随当前实例的定时器没有固定目标，而外部启动已经把该实例切为当前实例，因此直接视为覆盖。
        if (string.IsNullOrWhiteSpace(timerInstanceId))
            return true;

        return string.Equals(
            timerInstanceId.Trim(),
            startedInstanceId.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
