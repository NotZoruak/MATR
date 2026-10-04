using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 远征后台计时器识别：返回 true 表示倒计时已归零（或从未设置），应检查远征；
/// 返回 false 表示倒计时未到，跳过远征检查。
/// </summary>
public class ExpeditionTimerRecognition : IMaaCustomRecognition
{
    public string Name { get; set; } = nameof(ExpeditionTimerRecognition);

    private static DateTime? _nextCheckTime;

    /// <summary>启动倒计时</summary>
    public static void StartTimer(int intervalSeconds)
    {
        _nextCheckTime = DateTime.Now.AddSeconds(intervalSeconds);
    }

    /// <summary>重置计时器（任务启动时清零）</summary>
    public static void ResetTimer()
    {
        _nextCheckTime = null;
    }

    /// <summary>检查远征倒计时是否已到期</summary>
    public static bool IsExpired()
    {
        if (_nextCheckTime == null)
            return true;

        if (DateTime.Now >= _nextCheckTime.Value)
        {
            _nextCheckTime = null;
            return true;
        }

        return false;
    }

    public bool Analyze<T>(T context, in AnalyzeArgs args, in AnalyzeResults results) where T : IMaaContext
    {
        return IsExpired();
    }
}
