using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAAvalonia.Helper;
using MFAAvalonia.Services;
using System;

namespace MFAAvalonia.Extensions.MaaFW.Custom;

/// <summary>
/// 把日课项目标记为本次运行跳过。
/// 用于素材不足这类本次运行内无法解决、但下一个游戏日仍应重试的场景；
/// 它只写入内存中的运行期状态，不会改动每日完成台账。
/// </summary>
public sealed class DailyTaskStepSkipAction : IMaaCustomAction
{
    public string Name { get; set; } = nameof(DailyTaskStepSkipAction);

    public bool Run<T>(T context, in RunArgs args, in RunResults results) where T : IMaaContext
    {
        try
        {
            ActionParamHelper.ThrowIfStopping(context);
            var param = ActionParamHelper.Parse(args.ActionParam);
            var item = param["item"]?.ToObject<string>();
            var reason = param["reason"]?.ToObject<string>();
            if (string.IsNullOrWhiteSpace(item))
            {
                LoggerHelper.Error("[日课] 未提供本次运行跳过项目的标识");
                return false;
            }

            var instanceId = ActionParamHelper.ResolveOwnerInstanceId(context);
            DailyTaskCompletionService.MarkSkippedForCurrentRun(instanceId, item);
            LoggerHelper.Info(string.IsNullOrWhiteSpace(reason)
                ? $"[日课] 实例={DailyTaskCompletionService.NormalizeInstanceKey(instanceId)} 本次运行跳过项目：{item}"
                : $"[日课] 实例={DailyTaskCompletionService.NormalizeInstanceKey(instanceId)} 本次运行跳过项目：{item}（{reason}）");
            return true;
        }
        catch (MaaStopException)
        {
            return false;
        }
        catch (Exception exception)
        {
            LoggerHelper.Error($"[日课] 记录本次运行跳过失败：{exception.Message}");
            return false;
        }
    }
}
