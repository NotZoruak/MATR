using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;
using System.Threading;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 智能等待：读取 ExpeditionReturnTracker 的最早归队时间与全局 RefreshInterval，
/// 取较小值 sleep 后继续流水线。替代固定 post_delay 的 E_WaitRefresh。
/// </summary>
public class SmartWaitAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(SmartWaitAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);

            var json = ActionParamHelper.Parse(args.ActionParam);
            int intervalSeconds = (int?)json["interval"] ?? 600;
            int remainingSeconds = ExpeditionReturnTracker.GetRemainingSeconds();

            int waitSeconds;
            string startMsg;
            if (remainingSeconds > 0)
            {
                waitSeconds = Math.Min(remainingSeconds, intervalSeconds);
                startMsg = $"[远征计时] 最早归队 {FormatSeconds(remainingSeconds)}，实际等待 {FormatSeconds(waitSeconds)}";
            }
            else if (remainingSeconds == 0)
            {
                waitSeconds = 0;
                startMsg = "[远征计时] 检测到队伍已归队";
            }
            else
            {
                waitSeconds = intervalSeconds;
                startMsg = $"[远征计时] 无进行中的远征，间隔 {FormatSeconds(waitSeconds)}";
            }

            Log(context, startMsg);

            if (waitSeconds > 0)
            {
                // 记录等待窗口，供 MATR 层无响应检测排除合法静默；try/finally 保证停止、异常也清除窗口
                SmartWaitTracker.BeginWait(DateTime.Now.AddSeconds(waitSeconds));
                try
                {
                    // 分段 sleep 以响应停止信号
                    var deadline = DateTime.Now.AddSeconds(waitSeconds);
                    while (DateTime.Now < deadline)
                    {
                        ActionParamHelper.ThrowIfStopping(context);
                        var chunk = (int)Math.Min(5, (deadline - DateTime.Now).TotalSeconds);
                        if (chunk <= 0) break;
                        Thread.Sleep(chunk * 1000);
                    }
                }
                finally
                {
                    SmartWaitTracker.Clear();
                }
            }

            Log(context, "[远征计时] 倒计时结束");
            return true;
        }
        catch (MaaStopException)
        {
            Log(context, "[远征计时] 检测到手动停止");
            return false;
        }
        catch (Exception e)
        {
            Log(context, $"[远征计时] 错误: {e.Message}");
            return false;
        }
    }

    private static string FormatSeconds(int totalSeconds)
    {
        if (totalSeconds < 60)
            return $"{totalSeconds}秒";
        return $"{totalSeconds / 60}分{totalSeconds % 60}秒";
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
