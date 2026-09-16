using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 清空本次运行的跳过状态，由日课入口 node 在每次任务开始时执行。
/// 跳过状态只服务于当前这一次运行，任务结束后不保留，也不写入任何文件。
/// </summary>
public sealed class DailyTaskRunResetAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(DailyTaskRunResetAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var instanceId = ActionParamHelper.ResolveOwnerInstanceId(context);
            DailyTaskCompletionService.ClearRunSkips(instanceId);
            LoggerHelper.Info($"[日课] 实例={DailyTaskCompletionService.NormalizeInstanceKey(instanceId)} 已重置本次运行的跳过状态");
            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[日课] 重置本次运行跳过状态失败：{exception.Message}");
            return false;
        }
    }
}
