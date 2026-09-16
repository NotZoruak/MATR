using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>检查日课项目是否已在当前游戏日完成。</summary>
public sealed class DailyTaskCompletionCheckAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(DailyTaskCompletionCheckAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var item = ActionParamHelper.Parse(args.ActionParam)["item"]?.ToObject<string>();
            if (string.IsNullOrWhiteSpace(item))
            {
                LoggerHelper.Error("[日课] 未提供当日完成检查的项目标识");
                return false;
            }

            var instanceId = ActionParamHelper.ResolveOwnerInstanceId(context);
            var shouldRun = DailyTaskCompletionService.ShouldRun(
                instanceId,
                item,
                DateTime.Now);
            LoggerHelper.Info($"[日课] 实例={DailyTaskCompletionService.NormalizeInstanceKey(instanceId)}，项目={item}，当前游戏日{(shouldRun ? "未完成" : "已完成")}");
            return shouldRun;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[日课] 检查当日完成状态失败：{exception.Message}");
            return false;
        }
    }
}
