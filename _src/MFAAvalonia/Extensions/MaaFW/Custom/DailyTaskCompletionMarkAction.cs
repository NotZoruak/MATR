using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>将日课项目记录为当前游戏日已完成。</summary>
public sealed class DailyTaskCompletionMarkAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(DailyTaskCompletionMarkAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var item = ActionParamHelper.Parse(args.ActionParam)["item"]?.ToObject<string>();
            if (string.IsNullOrWhiteSpace(item))
            {
                LoggerHelper.Error("[日课] 未提供当日完成记录的项目标识");
                return false;
            }

            var instanceId = ActionParamHelper.ResolveOwnerInstanceId(context);
            DailyTaskCompletionService.MarkCompleted(
                instanceId,
                item,
                DateTime.Now);
            LoggerHelper.Info($"[日课] 实例={DailyTaskCompletionService.NormalizeInstanceKey(instanceId)}，项目={item} 已记录为当前游戏日完成");
            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[日课] 记录当日完成状态失败：{exception.Message}");
            return false;
        }
    }
}
