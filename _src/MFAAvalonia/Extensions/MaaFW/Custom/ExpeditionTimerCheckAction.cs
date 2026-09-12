using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 远征计时器检查动作：未过期返回 true 继续主任务；倒计时到期返回 false，走本 node 配置的 on_error 链路回本丸检查远征。
///
/// 到期是预期分支，而 MaaFW 的 SaveOnError 会对任何走 on_error 的 node 往 debug/on_error 写一张截图，
/// 因此每个智能调度周期都会多出一张 E_CheckTimerExpired 截图。
/// 2026-09-12 曾改为读本 node 的 on_error 目标再用 OverrideNext 改写成 next，但实机每次都会走到改写失败的兜底：
/// MaaFW 返回的 node 数据里 on_error 是 {"anchor":…,"jump_back":…,"name":…} 对象数组，按字符串解析必然抛异常。
/// 该改动已于 2026-09-13 整体撤回，行为回到直接 return false；后续再做这项优化时，必须能解析对象数组形式的 on_error，
/// 并在实机上确认 debug/on_error 内的 E_CheckTimerExpired 截图不再增长。
/// </summary>
public class ExpeditionTimerCheckAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(ExpeditionTimerCheckAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);

            if (ExpeditionTimerRecognition.IsExpired())
            {
                // 智能调度关闭时计时器从未启动，不输出误导性日志
                if (ExpeditionTimeTracker.IsSmartSchedulingEnabled())
                {
                    var msg = "[远征计时] 倒计时结束";
                    LoggerHelper.Info(msg);
                    try { ActionParamHelper.ResolveOwnerProcessor(context)?.AddLog(msg); } catch { }
                }

                return false;
            }

            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception e)
        {
            LoggerHelper.Error($"[远征计时] 错误: {e.Message}");
            return false;
        }
    }
}
