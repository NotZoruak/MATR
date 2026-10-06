using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 后勤完成时间追踪器：记录最早远征归队或内番完成时间，供 SmartWaitAction 计算等待时长。
/// </summary>
public static class ExpeditionReturnTracker
{
    private static DateTime? _earliestReturn;

    /// <summary>设置最早后勤完成时间</summary>
    public static void SetEarliestReturn(DateTime time)
    {
        _earliestReturn = time;
    }

    /// <summary>重置追踪器（任务启动或没有进行中的远征和内番时清零）</summary>
    public static void Reset()
    {
        _earliestReturn = null;
    }

    /// <summary>获取最早目标的剩余秒数，无目标或已过期时返回 0</summary>
    public static int GetRemainingSeconds()
    {
        if (_earliestReturn == null)
            return 0;

        var remaining = (_earliestReturn.Value - DateTime.Now).TotalSeconds;
        return remaining > 0 ? (int)Math.Ceiling(remaining) : 0;
    }

    /// <summary>是否有远征正在执行</summary>
    public static bool HasActiveExpedition()
    {
        return _earliestReturn != null && DateTime.Now < _earliestReturn.Value;
    }
}
