using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 后勤后台计时器识别：返回 true 表示倒计时已归零（或从未设置），应检查后勤；
/// 返回 false 表示倒计时未到，跳过后勤检查。
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
        try
        {
            ActionParamHelper.ThrowIfStopping(context);

            var expired = IsExpired();
            if (expired && ExpeditionTimeTracker.IsSmartSchedulingEnabled())
                Log(context, "[后勤计时] 倒计时结束");

            return expired;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[后勤计时] 识别失败: {exception.Message}");
            return false;
        }
    }

    private static void Log<T>(T context, string message) where T : IMaaContext
    {
        LoggerHelper.Info(message);
        try
        {
            ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(message);
        }
        catch { }
    }
}
